using SpacetimeDB;
using SpacetimeDB.Internal.TableHandles;

public static partial class Module
{
    /// <summary>
    /// This class represents a user. Users have an Identity, an optional name, and an online status
    /// </summary>
    [Table(Name = "user", Public = true)]
    public partial class User
    {
        [PrimaryKey]
        public Identity Client;
        public string? Name;
        public bool isActive;
    }

    /// <summary>
    /// This class is used to store the Identity of the user who sent the message, the Timestamp when it was sent, and the message text.
    /// </summary>
    [Table(Name = "cursor", Public = true)]
    public partial class Cursor
    {
        [Unique]
        public Identity Client;
        public float X;
        public float Y;
        public Timestamp LastUpdated;


    }

    [Reducer]
    public static void SetName(ReducerContext ctx, string name)
    {
        name = ValidateName(name);

        var user = ctx.Db.user.Client.Find(ctx.Sender);
        if (user is not null)
        {
            user.Name = name;
            ctx.Db.user.Client.Update(user);
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
    /// This will update the client's isActive status once they connect to the server
    /// </summary>
    /// <param name="ctx"></param>
    [Reducer(ReducerKind.ClientConnected)]
    public static void UserConnected(ReducerContext ctx)
    {
        Log.Info($"Connect {ctx.Sender}");
        var user = ctx.Db.user.Client.Find(ctx.Sender);
        if (user is null)
        {
            ctx.Db.user.Insert(new User { Client = ctx.Sender, Name = null, isActive = true });
        }
        else
        {
            user.isActive = true;
            ctx.Db.user.Client.Update(user);
        }
    }
    /// <summary>
    /// This reducer will un-set the isActive status of the client when they disconnect.
    /// </summary>
    /// <param name="ctx"></param>
    public static void UserDisconnected(ReducerContext ctx)
    {
        Log.Info($"Disconnect {ctx.Sender}");
        var user = ctx.Db.user.Client.Find(ctx.Sender);

        if (user is not null)
        {
            user.isActive = false;
            ctx.Db.user.Client.Update(user);
        }
    }

    public static void UpdateCursor(ReducerContext ctx, float x, float y)
    {
        var user = ctx.Db.user.Client.Find(ctx.Sender);
        var cursor = ctx.Db.cursor.Insert(new Cursor());
        if (cursor is not null)
        {
            cursor.X = x;
            cursor.Y = y;
            cursor.LastUpdated = ctx.Timestamp;
            ctx.Db.cursor.Client.Update(cursor);
        }
        else
        {
            ctx.Db.cursor.Insert(new Cursor { Client = ctx.Sender, X = x, Y = y, LastUpdated = ctx.Timestamp });
        }
        if (user is not null)
        {
            user.isActive = true;
            ctx.Db.user.Client.Update(user);
        }
        if ((user is not null) && (ctx.Timestamp.CompareTo(cursor.LastUpdated) > 2000000))
        {
            TimeSpan difference = ctx.Timestamp - cursor.LastUpdated;
            user.isActive = false;
            ctx.Db.user.Client.Update(user);
            // Do not draw cursor
        }

    }

}


