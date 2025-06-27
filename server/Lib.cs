using SpacetimeDB;

public static partial class Module
{
    /// <summary>
    /// This class represents a user. Users have an Identity, an optional name, and an online status
    /// </summary>
    [Table(Name = "user", Public = true)]
    public partial class User
    {
        [PrimaryKey]
        public Identity Identity;
        public string? Name;
        public bool Online;
    }

    /// <summary>
    /// This class is used to store the Identity of the user who sent the message, the Timestamp when it was sent, and the message text.
    /// </summary>
    [Table(Name = "message", Public = true)]
    public partial class Message
    {
        public Identity Sender;
        public Timestamp Sent;
        public string Text = "";
    }

    [Reducer]
    public static void SetName(ReducerContext ctx, string name)
    {
        name = ValidateName(name);

        var user = ctx.Db.user.Identity.Find(ctx.Sender);
        if (user is not null)
        {
            user.Name = name;
            ctx.Db.user.Identity.Update(user);
        }
    }

    /// Takes a name and checks if it's acceptable as a user's name.
    private static string ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new Exception("Names must not be empty");
        }
        return name;
    }

    /// <summary>
    /// A client will call this reducer to send messages. It validates the text, then inserts a new Message record with the sender, identity, and time. 
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="text"></param>
    [Reducer]
    public static void SendMessage(ReducerContext ctx, string text)
    {
        text = ValidateMessage(text);
        Log.Info(text);
        ctx.Db.message.Insert(
            new Message
            {
                Sender = ctx.Sender,
                Text = text,
                Sent = ctx.Timestamp,
            }
        );
    }

    /// Takes a message's text and checks if it's acceptable to send.
    private static string ValidateMessage(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Messages must not be empty");
        }
        return text;
    }

    /// <summary>
    /// This will update the client's online status once they connect to the server
    /// </summary>
    /// <param name="ctx"></param>
    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        Log.Info($"Connect {ctx.Sender}");
        var user = ctx.Db.user.Identity.Find(ctx.Sender);

        if (user is not null)
        {
            // If this is a returning user, i.e., we already have a `User` with this `Identity`,
            // set `Online: true`, but leave `Name` and `Identity` unchanged.
            user.Online = true;
            ctx.Db.user.Identity.Update(user);
        }
        else
        {
            // If this is a new user, create a `User` object for the `Identity`,
            // which is online, but hasn't set a name.
            ctx.Db.user.Insert(
                new User
                {
                    Name = null,
                    Identity = ctx.Sender,
                    Online = true,
                }
            );
        }
    }

    /// <summary>
    /// This reducer will un-set the online status of the client when they disconnect.
    /// </summary>
    /// <param name="ctx"></param>
    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        var user = ctx.Db.user.Identity.Find(ctx.Sender);

        if (user is not null)
        {
            // This user should exist, so set `Online: false`.
            user.Online = false;
            ctx.Db.user.Identity.Update(user);
        }
        else
        {
            // User does not exist, log warning
            Log.Warn("Warning: No user found for disconnected client.");
        }
    }

}


