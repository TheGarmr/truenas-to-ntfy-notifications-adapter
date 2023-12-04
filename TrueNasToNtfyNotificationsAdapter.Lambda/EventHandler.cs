using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using HtmlAgilityPack;
using Markdig;
using Newtonsoft.Json;
using ntfy;
using ntfy.Actions;
using ntfy.Requests;
using Action = ntfy.Actions.Action;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TrueNasToNtfyNotificationsAdapter.Lambda;

public class EventHandler
{
    private readonly string _ntfyBaseUrl = Environment.GetEnvironmentVariable("NTFY_BASE_URL")!;
    private readonly string _ntfyTopicName = Environment.GetEnvironmentVariable("NTFY_TOPIC_NAME")!;
    private readonly string _trueNasBaseUrl = Environment.GetEnvironmentVariable("TRUE_NAS_BASE_URL")!;
    private const string DefaultHeaderTag = "mailbox_with_mail";
    private const string ErrorHeaderTag = "red_circle";
    private const string IncorrectSnsEventDefaultMessage = $"{nameof(TrueNasToNtfyNotificationsAdapter)}: incorrect sns event.";
    private const string IncorrectSnsEventMessageDefaultMessage = $"{nameof(TrueNasToNtfyNotificationsAdapter)}: incorrect sns event's message.";

    public async Task HandleEvent(SNSEvent snsEvent, ILambdaContext context)
    {
        context.Logger.Log(JsonConvert.SerializeObject(snsEvent));

        if (snsEvent?.Records == null || snsEvent.Records.Count == 0)
        {
            context.Logger.Log(IncorrectSnsEventDefaultMessage);
            await SendNotificationAsync(ErrorToNtfyMessage(IncorrectSnsEventDefaultMessage, PriorityLevel.Default), context);
            return;
        }

        foreach (var record in snsEvent.Records)
        {
            if (string.IsNullOrWhiteSpace(record?.Sns?.Message))
            {
                await SendNotificationAsync(ErrorToNtfyMessage(IncorrectSnsEventMessageDefaultMessage, PriorityLevel.Default), context);
                continue;
            }

            var notificationMessage = await EventMessageToNtfyMessage(record.Sns.Message, PriorityLevel.Default, context);
            await SendNotificationAsync(notificationMessage, context);
        }
    }

    private async Task SendNotificationAsync(SendingMessage message, ILambdaContext context)
    {
        try
        {
            var client = new Client(_ntfyBaseUrl);
            await client.Publish(_ntfyTopicName, message);
        }
        catch (Exception ex)
        {
            context.Logger.Log($"Notification request wasn't sent: {ex.Message}");
        }
    }

    private async Task<SendingMessage> EventMessageToNtfyMessage(string eventMessage, PriorityLevel priorityLevel, ILambdaContext context)
    {
        try
        {
            var titleWithTrueNasName = eventMessage.Split("<br><br>")[0];
            var title = titleWithTrueNasName.Replace("TrueNAS @ ", string.Empty);
            var ntfyText = HtmlMessageToNotificationMessage(eventMessage, titleWithTrueNasName);
            var message = new SendingMessage
            {
                Title = title,
                Priority = priorityLevel,
                Message = ntfyText,
                Tags = new[] { DefaultHeaderTag },
                Actions = new Action[] { new View($"Open {title}", new Uri(_trueNasBaseUrl)) }
            };
            return message;
        }
        catch (Exception ex)
        {
            var logErrorMessage = $"Notification wasn't processed. Error while processing sns event: {ex.Message}";
            context.Logger.Log(logErrorMessage);
            var errorMessage = ErrorToNtfyMessage(logErrorMessage, PriorityLevel.Default);
            await SendNotificationAsync(errorMessage, context);
            throw;
        }
    }

    private string HtmlMessageToNotificationMessage(string eventMessage, string titleWithTrueNasName)
    {
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(Markdown.ToHtml(eventMessage.Replace(titleWithTrueNasName, string.Empty)));
        return htmlDocument.DocumentNode.InnerText;
    }

    private static SendingMessage ErrorToNtfyMessage(string errorText, PriorityLevel priorityLevel)
    {
        return new SendingMessage
        {
            Title = nameof(TrueNasToNtfyNotificationsAdapter),
            Priority = priorityLevel,
            Message = errorText,
            Tags = new[] { ErrorHeaderTag }
        };
    }
}