using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CreateExpense;

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
            // ── 1. Lire les claims JWT (RBAC) ──────────────────────────
            var claims = request.RequestContext.Authorizer.Claims;

            // Seul un employé peut créer une dépense
            var groups = claims.ContainsKey("cognito:groups")
                ? claims["cognito:groups"] : "";

            if (groups.Contains("finance"))
                return Error(403, "Finance managers cannot create expenses");

            var userId   = claims["sub"];
            var userEmail = claims.ContainsKey("email") ? claims["email"] : "";

            // ── 2. Parser le body ──────────────────────────────────────
            var body = JsonSerializer.Deserialize<CreateExpenseRequest>(
                request.Body ?? "{}");

            if (body == null || body.Amount <= 0 || string.IsNullOrEmpty(body.Category))
                return Error(400, "Amount and Category are required");

            // ── 3. Construire l'item DynamoDB ──────────────────────────
            var expenseId = Guid.NewGuid().ToString();
            var now       = DateTime.UtcNow.ToString("o");

            var item = new Dictionary<string, AttributeValue>
            {
                ["PK"]          = new AttributeValue { S = $"USER#{userId}" },
                ["SK"]          = new AttributeValue { S = $"EXPENSE#{expenseId}" },
                ["expenseId"]   = new AttributeValue { S = expenseId },
                ["userId"]      = new AttributeValue { S = userId },
                ["userEmail"]   = new AttributeValue { S = userEmail },
                ["status"]      = new AttributeValue { S = "DRAFT" },
                ["amount"]      = new AttributeValue { N = body.Amount.ToString() },
                ["category"]    = new AttributeValue { S = body.Category },
                ["description"] = new AttributeValue { S = body.Description ?? "" },
                ["createdAt"]   = new AttributeValue { S = now },
                ["updatedAt"]   = new AttributeValue { S = now },
            };

            await _dynamo.PutItemAsync(TABLE, item);

            // ── 4. Retourner la réponse ────────────────────────────────
            var response = new
            {
                expenseId,
                userId,
                status    = "DRAFT",
                amount    = body.Amount,
                category  = body.Category,
                description = body.Description,
                createdAt = now
            };

            return Ok(201, response);
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
                ["Content-Type"]                 = "application/json",
                ["Access-Control-Allow-Origin"]  = "*"
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
public class CreateExpenseRequest
{
    public decimal Amount      { get; set; }
    public string  Category    { get; set; } = "";
    public string  Description { get; set; } = "";
}
