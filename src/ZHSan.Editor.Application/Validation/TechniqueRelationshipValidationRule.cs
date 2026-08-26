using System.Reflection;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public sealed class TechniqueRelationshipValidationRule : ICrossTableValidationRule
{
    private const string TechniqueConfigKey = "techniques";
    private const string PreIdProperty = "PreID";
    private const string PostIdProperty = "PostID";

    public IEnumerable<ValidationIssue> Validate(CrossTableValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Tables.TryGetValue(TechniqueConfigKey, out var table))
        {
            return [];
        }

        var records = table.Items
            .Where(item => item.Id.HasValue)
            .Select(item => new TechniqueRecord(
                item,
                GetIntProperty(item.Value, PreIdProperty),
                GetIntProperty(item.Value, PostIdProperty)))
            .ToArray();
        var uniqueRecords = records
            .GroupBy(record => record.Item.Id!.Value)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var issues = new List<ValidationIssue>();

        foreach (var record in records)
        {
            var id = record.Item.Id!.Value;
            ValidateSelfReference(record, id, issues);
            ValidatePredecessor(record, id, uniqueRecords, issues);
            ValidateSuccessor(record, id, uniqueRecords, issues);
        }

        issues.AddRange(ValidateCycles(uniqueRecords));
        return issues;
    }

    private static void ValidateSelfReference(
        TechniqueRecord record,
        int id,
        ICollection<ValidationIssue> issues)
    {
        if (record.PreId == id)
        {
            issues.Add(CreateIssue(id, PreIdProperty, $"前置科技不能引用自身 ID {id}。"));
        }

        if (record.PostId == id)
        {
            issues.Add(CreateIssue(id, PostIdProperty, $"后置科技不能引用自身 ID {id}。"));
        }
    }

    private static void ValidatePredecessor(
        TechniqueRecord record,
        int id,
        IReadOnlyDictionary<int, TechniqueRecord> records,
        ICollection<ValidationIssue> issues)
    {
        if (record.PreId == 0 || record.PreId == id || !records.TryGetValue(record.PreId, out var predecessor))
        {
            return;
        }

        if (predecessor.PostId != id)
        {
            issues.Add(CreateIssue(
                id,
                PreIdProperty,
                $"前置科技 ID {record.PreId} 的 {PostIdProperty} 应为 {id}，当前为 {predecessor.PostId}。"));
        }
    }

    private static void ValidateSuccessor(
        TechniqueRecord record,
        int id,
        IReadOnlyDictionary<int, TechniqueRecord> records,
        ICollection<ValidationIssue> issues)
    {
        if (record.PostId == 0 || record.PostId == id || !records.TryGetValue(record.PostId, out var successor))
        {
            return;
        }

        if (successor.PreId != id)
        {
            issues.Add(CreateIssue(
                id,
                PostIdProperty,
                $"后置科技 ID {record.PostId} 的 {PreIdProperty} 应为 {id}，当前为 {successor.PreId}。"));
        }
    }

    private static IEnumerable<ValidationIssue> ValidateCycles(
        IReadOnlyDictionary<int, TechniqueRecord> records)
    {
        var edges = CreateEdges(records);
        var adjacency = records.Keys.ToDictionary(
            id => id,
            id => (IReadOnlyList<int>)edges
                .Where(edge => edge.FromId == id)
                .Select(edge => edge.ToId)
                .Distinct()
                .Order()
                .ToArray());

        foreach (var component in FindStronglyConnectedComponents(adjacency)
                     .Where(component => component.Count > 1)
                     .OrderBy(component => component.Min()))
        {
            var ids = component.ToHashSet();
            var message = $"科技关系存在循环依赖，涉及 ID：{string.Join("、", component.Order())}。";
            foreach (var edge in edges
                         .Where(edge => ids.Contains(edge.FromId) && ids.Contains(edge.ToId))
                         .GroupBy(edge => (edge.FromId, edge.ToId))
                         .Select(group => group
                             .OrderBy(edge => edge.PropertyName == PreIdProperty ? 0 : 1)
                             .ThenBy(edge => edge.SourceRecordId)
                             .First())
                         .OrderBy(edge => edge.SourceRecordId)
                         .ThenBy(edge => edge.PropertyName, StringComparer.Ordinal))
            {
                yield return CreateIssue(edge.SourceRecordId, edge.PropertyName, message);
            }
        }
    }

    private static IReadOnlyList<RelationshipEdge> CreateEdges(
        IReadOnlyDictionary<int, TechniqueRecord> records)
    {
        var edges = new List<RelationshipEdge>();
        foreach (var (id, record) in records.OrderBy(pair => pair.Key))
        {
            if (record.PreId != 0 && record.PreId != id && records.ContainsKey(record.PreId))
            {
                edges.Add(new RelationshipEdge(id, record.PreId, id, PreIdProperty));
            }

            if (record.PostId != 0 && record.PostId != id && records.ContainsKey(record.PostId))
            {
                edges.Add(new RelationshipEdge(record.PostId, id, id, PostIdProperty));
            }
        }

        return edges;
    }

    private static IReadOnlyList<IReadOnlyList<int>> FindStronglyConnectedComponents(
        IReadOnlyDictionary<int, IReadOnlyList<int>> adjacency)
    {
        var nextIndex = 0;
        var indices = new Dictionary<int, int>();
        var lowLinks = new Dictionary<int, int>();
        var stack = new Stack<int>();
        var onStack = new HashSet<int>();
        var components = new List<IReadOnlyList<int>>();

        foreach (var id in adjacency.Keys.Order())
        {
            if (!indices.ContainsKey(id))
            {
                Visit(id);
            }
        }

        return components;

        void Visit(int id)
        {
            indices[id] = nextIndex;
            lowLinks[id] = nextIndex;
            nextIndex++;
            stack.Push(id);
            onStack.Add(id);

            foreach (var targetId in adjacency[id])
            {
                if (!indices.ContainsKey(targetId))
                {
                    Visit(targetId);
                    lowLinks[id] = Math.Min(lowLinks[id], lowLinks[targetId]);
                }
                else if (onStack.Contains(targetId))
                {
                    lowLinks[id] = Math.Min(lowLinks[id], indices[targetId]);
                }
            }

            if (lowLinks[id] != indices[id])
            {
                return;
            }

            var component = new List<int>();
            int memberId;
            do
            {
                memberId = stack.Pop();
                onStack.Remove(memberId);
                component.Add(memberId);
            }
            while (memberId != id);

            components.Add(component);
        }
    }

    private static int GetIntProperty(object item, string propertyName)
    {
        var property = item.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetValue(item) is int value)
        {
            return value;
        }

        throw new InvalidOperationException(
            $"科技配置类型 {item.GetType().FullName} 不包含整数属性 {propertyName}。");
    }

    private static ValidationIssue CreateIssue(int itemId, string propertyName, string message) =>
        new(ValidationSeverity.Error, TechniqueConfigKey, itemId, propertyName, message);

    private sealed record TechniqueRecord(ValidationItem Item, int PreId, int PostId);

    private sealed record RelationshipEdge(
        int FromId,
        int ToId,
        int SourceRecordId,
        string PropertyName);
}
