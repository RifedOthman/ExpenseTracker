using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;
 
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
 
namespace SubmitExpense;
 
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
            // ── 1. RBAC : seul un employé peut soumettre ───────────────
            var claims  = request.RequestContext.Authorizer.Claims;
            var userId  = claims["sub"];
            var groups  = claims.ContainsKey("cognito:groups")
                ? claims["cognito:groups"] : "";
 
            if (groups.Contains("finance"))
                return Error(403, "Finance managers cannot submit expenses");
 
            // ── 2. Lire l'expenseId depuis l'URL ──────────────────────
            var expenseId = request.PathParameters?["expenseId"];
            if (string.IsNullOrEmpty(expenseId))
                return Error(400, "expenseId is required");
 
            // ── 3. Récupérer la dépense existante ─────────────────────
            var getResult = await _dynamo.GetItemAsync(TABLE,
                new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                    ["SK"] = new AttributeValue { S = $"EXPENSE#{expenseId}" }
                });
 
            if (!getResult.IsItemSet || getResult.Item.Count == 0)
                return Error(404, "Expense not found");
 
            var item = getResult.Item;
 
            // ── 4. STATE MACHINE : valider la transition ───────────────
            var currentStatus = item["status"].S;
 
            // Transitions autorisées vers SUBMITTED : DRAFT et REJECTED
            if (currentStatus != "DRAFT" && currentStatus != "REJECTED")
                return Error(400,
                    $"Cannot submit expense with status '{currentStatus}'. " +
                    "Only DRAFT or REJECTED expenses can be submitted.");
 
            // ── 5. Mettre à jour le statut dans DynamoDB ───────────────
            var now = DateTime.UtcNow.ToString("o");
 
            await _dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = TABLE,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"USER#{userId}" },
                    ["SK"] = new AttributeValue { S = $"EXPENSE#{expenseId}" }
                },
                UpdateExpression = "SET #s = :newStatus, updatedAt = :now",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#s"] = "status"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":newStatus"]     = new AttributeValue { S = "SUBMITTED" },
                    [":now"]           = new AttributeValue { S = now },
                    [":currentStatus"] = new AttributeValue { S = currentStatus }
                },
                // Protection contre les race conditions
                ConditionExpression = "#s = :currentStatus"
            });
 
            return Ok(200, new
            {
                expenseId,
                previousStatus = currentStatus,
                newStatus      = "SUBMITTED",
                updatedAt      = now,
                message        = "Expense submitted successfully"
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
