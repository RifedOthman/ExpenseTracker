using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;
 
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
 
namespace ListExpenses;
 
public class Function
{
    private readonly AmazonDynamoDBClient _dynamo;
    private const string TABLE = "Expenses";
    private const string GSI   = "status-createdAt-index";
 
    public Function()
    {
        _dynamo = new AmazonDynamoDBClient();
    }
 
    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            // ── 1. Lire les claims JWT ─────────────────────────────────
            var claims = request.RequestContext.Authorizer.Claims;
            var userId  = claims["sub"];
            var groups  = claims.ContainsKey("cognito:groups")
                ? claims["cognito:groups"] : "";
            var isFinance = groups.Contains("finance");
 
            List<Dictionary<string, AttributeValue>> items;
 
            if (isFinance)
            {
                // ── Finance : voit toutes les dépenses SUBMITTED via GSI
                var queryRequest = new QueryRequest
                {
                    TableName              = TABLE,
                    IndexName              = GSI,
                    KeyConditionExpression = "#s = :status",
                    ExpressionAttributeNames  = new Dictionary<string, string>
                    {
                        ["#s"] = "status"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":status"] = new AttributeValue { S = "SUBMITTED" }
                    },
                    ScanIndexForward = false // plus récent en premier
                };
                var result = await _dynamo.QueryAsync(queryRequest);
                items = result.Items;
            }
            else
            {
                // ── Employé : voit uniquement ses propres dépenses
                var queryRequest = new QueryRequest
                {
                    TableName              = TABLE,
                    KeyConditionExpression = "PK = :pk",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":pk"] = new AttributeValue { S = $"USER#{userId}" }
                    },
                    ScanIndexForward = false
                };
                var result = await _dynamo.QueryAsync(queryRequest);
                items = result.Items;
            }
 
            // ── 2. Mapper les items en objets simples ──────────────────
            var expenses = items.Select(item => new
            {
                expenseId   = GetStr(item, "expenseId"),
                userId      = GetStr(item, "userId"),
                userEmail   = GetStr(item, "userEmail"),
                status      = GetStr(item, "status"),
                amount      = GetNum(item, "amount"),
                category    = GetStr(item, "category"),
                description = GetStr(item, "description"),
                createdAt   = GetStr(item, "createdAt"),
                updatedAt   = GetStr(item, "updatedAt"),
                justification = GetStr(item, "justification"),
            }).ToList();
 
            return Ok(200, new { expenses, count = expenses.Count });
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error: {ex.Message}");
            return Error(500, "Internal server error");
        }
    }
 
    // ── Helpers ────────────────────────────────────────────────────────
    private static string GetStr(Dictionary<string, AttributeValue> item, string key) =>
        item.ContainsKey(key) ? item[key].S ?? "" : "";
 
    private static decimal GetNum(Dictionary<string, AttributeValue> item, string key) =>
        item.ContainsKey(key) && decimal.TryParse(item[key].N, out var v) ? v : 0;
 
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
 




















