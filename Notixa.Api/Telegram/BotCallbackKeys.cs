namespace Notixa.Api.Telegram;

public static class BotCallbackKeys
{
    public const string Home = "screen:home";
    public const string Subscriptions = "screen:subscriptions";
    public const string Services = "screen:services";
    public const string ServiceViewPrefix = "service:view:";
    public const string ServiceAdminsPrefix = "service:admins:";
    public const string ServiceGeneralInvitePrefix = "service:invite:general:";
    public const string UnsubscribeAskPrefix = "unsub:ask:";
    public const string UnsubscribeYesPrefix = "unsub:yes:";
    public const string UnsubscribeNoPrefix = "unsub:no:";
    public const string SubscribeYesPrefix = "sub:yes:";
    public const string SubscribeNoPrefix = "sub:no:";
    public const string CreatorManagement = "screen:creators";

    public const string FlowCancel = "flow:cancel";
    public const string FlowBack = "flow:back";
    public const string FlowStartCreateService = "flow:start:create-service";
    public const string FlowStartCreateTemplatePrefix = "flow:start:create-template:";
    public const string FlowStartPersonalInvitePrefix = "flow:start:personal-invite:";
    public const string FlowStartAddAdminPrefix = "flow:start:add-admin:";
    public const string FlowStartRemoveAdminPrefix = "flow:start:remove-admin:";
    public const string FlowStartCreatorAllow = "flow:start:creator:allow";
    public const string FlowStartCreatorDeny = "flow:start:creator:deny";
    public const string FlowTemplateParseModePrefix = "flow:template:parse:";
    public const string FlowPersonalInviteExpiryNone = "flow:personal-invite:expiry:none";
    public const string FlowPersonalInviteExpiryManual = "flow:personal-invite:expiry:manual";
    public const string FlowRemoveAdminManual = "flow:remove-admin:manual";
    public const string FlowRemoveAdminSelectPrefix = "flow:remove-admin:select:";
    public const string FlowCreateServiceConfirm = "flow:create-service:confirm";
}
