using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
 
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
 
namespace GetReceiptUrl;
 
public class Function
{
    private readonly AmazonDynamoDBClient _dynamo;
    private readonly AmazonS3Client _s3;
    private const string TABLE  = "Expenses";
    private const string BUCKET = "expense-receipts-rifed"; 
 
    public Function()
    {
        _dynamo = new AmazonDynamoDBClient();
        _s3     = new AmazonS3Client();
    }
 
    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            // ── 1. Lire les claims JWT ─────────────────────────────────
            var claims    = request.RequestContext.Authorizer.Claims;
            var userId    = claims["sub"];
            var groups    = claims.ContainsKey("cognito:groups")
                ? claims["cognito:groups"] : "";
            var isFinance = groups.Contains("finance");
 
            // ── 2. Lire expenseId + action (upload ou download) ────────
            var expenseId = request.PathParameters?["expenseId"];
            if (string.IsNullOrEmpty(expenseId))
                return Error(400, "expenseId is required");
 
            // action = "upload" ou "download"
            var action = request.QueryStringParameters
                ?.ContainsKey("action") == true
                ? request.QueryStringParameters["action"]
                : "download";
 
            // ── 3. Récupérer la dépense ────────────────────────────────
            var scanRequest = new ScanRequest
            {
                TableName        = TABLE,
                FilterExpression = "expenseId = :eid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":eid"] = new AttributeValue { S = expenseId }
                }
            };
            var scanResult = await _dynamo.ScanAsync(scanRequest);
 
            if (scanResult.Items.Count == 0)
                return Error(404, "Expense not found");
 
            var item     = scanResult.Items[0];
            var ownerId  = item["userId"].S;
 
            // ── 4. Vérifier les droits d'accès ────────────────────────
            // Employé : accès uniquement à ses propres dépenses
            // Finance : accès à toutes les dépenses
            if (!isFinance && ownerId != userId)
                return Error(403, "Access denied");
 
            // ── 5. Générer la clé S3 ───────────────────────────────────
            var receiptKey = item.ContainsKey("receiptKey")
                ? item["receiptKey"].S
                : $"receipts/{expenseId}.jpg";
 
            string presignedUrl;
 
            if (action == "upload")
            {
                // ── Générer une URL de PUT (upload) ────────────────────
                var uploadRequest = new GetPreSignedUrlRequest
                {
                    BucketName = BUCKET,
                    Key        = $"receipts/{expenseId}.jpg",
                    Verb       = HttpVerb.PUT,
                    Expires    = DateTime.UtcNow.AddMinutes(15),
                    ContentType = "image/jpeg"
                };
                presignedUrl = _s3.GetPreSignedURL(uploadRequest);
 
                // Mettre à jour la clé du reçu dans DynamoDB
                await _dynamo.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = TABLE,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = new AttributeValue { S = item["PK"].S },
                        ["SK"] = new AttributeValue { S = item["SK"].S }
                    },
                    UpdateExpression = "SET receiptKey = :key, updatedAt = :now",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":key"] = new AttributeValue { S = $"receipts/{expenseId}.jpg" },
                        [":now"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    }
                });
 
                return Ok(200, new
                {
                    action      = "upload",
                    presignedUrl,
                    expireIn    = "15 minutes",
                    message     = "Use PUT request with Content-Type: image/jpeg to upload"
                });
            }
            else
            {
                // ── Générer une URL de GET (download/affichage) ────────
                var downloadRequest = new GetPreSignedUrlRequest
                {
                    BucketName = BUCKET,
                    Key        = receiptKey,
                    Verb       = HttpVerb.GET,
                    Expires    = DateTime.UtcNow.AddMinutes(15)
                };
                presignedUrl = _s3.GetPreSignedURL(downloadRequest);
 
                return Ok(200, new
                {
                    action      = "download",
                    presignedUrl,
                    expireIn    = "15 minutes",
                    message     = "URL expires in 15 minutes"
                });
            }
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error: {ex.Message}");
            return Error(500, "Internal server error");
        }
    }
 
    // ── Helpers ────────────────────────────────────────────────────────
    private static APIGatewayProxyResponse Ok(int statusCode, object body) =>
        new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Body       = JsonSerializer.Serialize(body),
            Headers    = new Dictionary<string, string>
            {
                ["Content-Type"]                = "application/json",
                ["Access-Control-Allow-Origin"] = "*"
            }
        };
 
    private static APIGatewayProxyResponse Error(int statusCode, string message) =>
        new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Body       = JsonSerializer.Serialize(new { message }),
            Headers    = new Dictionary<string, string>
            {
                ["Content-Type"]                = "application/json",
                ["Access-Control-Allow-Origin"] = "*"
            }
        };
}
 




















