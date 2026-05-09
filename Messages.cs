using CommunityToolkit.Mvvm.Messaging.Messages;

namespace PosSystem;

public sealed class LoginSuccessMessage : ValueChangedMessage<string>
{
    public LoginSuccessMessage(string userName) : base(userName) { }
}

public sealed class LogoutMessage { }

public sealed class SessionExpiredMessage { }
