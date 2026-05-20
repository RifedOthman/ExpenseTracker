using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;
 
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
 
namespace RejectExpense;
 
public class Function
{
    private readonly AmazonDynamoDBClient _dynamo;
    private const string TABLE = "Expenses";
 
    public Function()
    {
        _dynamo = new AmazonDynamoDBClient();
    }
 
    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            // ── 1. RBAC : seul finance peut rejeter ───────────────────
            var claims = request.RequestContext.Authorizer.Claims;
            var groups = claims.ContainsKey("cognito:groups")
                ? claims["cognito:groups"] : "";
 
            if (!groups.Contains("finance"))
                return Error(403, "Only finance managers can reject expenses");
 
            var managerId    = claims["sub"];
            var managerEmail = claims.ContainsKey("email") ? claims["email"] : "";
 
            // ── 2. Lire expenseId + justification ─────────────────────
            var expenseId = request.PathParameters?["expenseId"];
            if (string.IsNullOrEmpty(expenseId))
                return Error(400, "expenseId is required");
 
            var body = JsonSerializer.Deserialize<RejectRequest>(
                request.Body ?? "{}");
 
            if (string.IsNullOrEmpty(body?.Justification))
                return Error(400, "Justification is required");
 
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
 
            var item          = scanResult.Items[0];
            var pk            = item["PK"].S;
            var sk            = item["SK"].S;
            var currentStatus = item["status"].S;
 
            // ── 4. STATE MACHINE : SUBMITTED → REJECTED uniquement ────
            if (currentStatus != "SUBMITTED")
                return Error(400,
                    $"Cannot reject expense with status '{currentStatus}'. " +
                    "Only SUBMITTED expenses can be rejected.");
 
            // ── 5. Mettre à jour DynamoDB ──────────────────────────────
            var now = DateTime.UtcNow.ToString("o");
 
            await _dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = TABLE,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = pk },
                    ["SK"] = new AttributeValue { S = sk }
                },
                UpdateExpression =
                    "SET #s = :newStatus, updatedAt = :now, " +
                    "justification = :just, managerId = :mid, managerEmail = :memail",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#s"] = "status"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":newStatus"]     = new AttributeValue { S = "REJECTED" },
                    [":now"]           = new AttributeValue { S = now },
                    [":just"]          = new AttributeValue { S = body.Justification },
                    [":mid"]           = new AttributeValue { S = managerId },
                    [":memail"]        = new AttributeValue { S = managerEmail },
                    [":currentStatus"] = new AttributeValue { S = "SUBMITTED" }
                },
                ConditionExpression = "#s = :currentStatus"
            });
 
            return Ok(200, new
            {
                expenseId,
                previousStatus = "SUBMITTED",
                newStatus      = "REJECTED",
                justification  = body.Justification,
                managerId,
                updatedAt      = now,
                message        = "Expense rejected. Employee can resubmit after correction."
            });
        }
        catch (ConditionalCheckFailedException)
        {
            return Error(409, "Expense status changed concurrently. Please retry.");
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
 
// ── DTO ───────────────────────────────────────────────────────────────
public class RejectRequest
{
    public string Justification { get; set; } = "";
}
