namespace Vktun.IoT.Connector.Communication.Mqtt;

public static class MqttTopicFilter
{
    public static bool IsMatch(string topicFilter, string topic)
    {
        if (string.IsNullOrWhiteSpace(topicFilter) || string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var filterParts = topicFilter.Split('/');
        var topicParts = topic.Split('/');

        for (var index = 0; index < filterParts.Length; index++)
        {
            var filterPart = filterParts[index];
            if (filterPart == "#")
            {
                return index == filterParts.Length - 1;
            }

            if (index >= topicParts.Length)
            {
                return false;
            }

            if (filterPart == "+")
            {
                continue;
            }

            if (!filterPart.Equals(topicParts[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return filterParts.Length == topicParts.Length;
    }
}
