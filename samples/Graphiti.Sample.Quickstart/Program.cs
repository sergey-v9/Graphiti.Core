using Graphiti.Core;
using Graphiti.Core.Drivers;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;

const string GroupId = "quickstart";

await using var graphiti = new global::Graphiti.Core.Graphiti(
    graphDriver: new InMemoryGraphDriver("quickstart"));

await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);

var now = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);
var maya = new EntityNode
{
    Name = "Maya Patel",
    GroupId = GroupId,
    Labels = new List<string> { "Person" },
    CreatedAt = now,
    Summary = "Maya Patel manages the Atlas migration project."
};
var atlas = new EntityNode
{
    Name = "Atlas migration",
    GroupId = GroupId,
    Labels = new List<string> { "Project" },
    CreatedAt = now,
    Summary = "Atlas is a migration project at Nimbus Health."
};
var manages = new EntityEdge
{
    Name = "MANAGES",
    Fact = "Maya Patel manages the Atlas migration project.",
    GroupId = GroupId,
    CreatedAt = now,
    ValidAt = now
};

await graphiti.AddTripletAsync(maya, manages, atlas);

var facts = await graphiti.SearchAsync(
    "Who manages Atlas?",
    groupIds: new[] { GroupId },
    numResults: 3);

Console.WriteLine("Hello graph facts");
foreach (var fact in facts)
{
    Console.WriteLine($"- {fact.Fact}");
}
