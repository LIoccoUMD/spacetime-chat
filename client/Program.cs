using SpacetimeDB;
using SpacetimeDB.Types;
using System.Collections.Concurrent;

// Local client SpacetimeDB identity
Identity? local_identity = null;

// Declare a thread safe queue to store commands
var input_queue = new ConcurrentQueue<(string Command, string Args)>();

void Main()
{
    // Initialize the `AuthToken` module
    AuthToken.Init(".spacetime_csharp_cursorchaos");
    // Builds and connects to the database
    DbConnection? conn = null;
    conn = ConnectToDB();
    // Registers to run in response to database events.
    RegisterCallbacks(conn);
    // Declare a threadsafe cancel token to cancel the process loop
    var cancellationTokenSource = new CancellationTokenSource();
    // Spawn a thread to call process updates and process commands
    var thread = new Thread(() => ProcessThread(conn, cancellationTokenSource.Token));
    thread.Start();
    // Handles CLI input
    InputLoop();
    // This signals the ProcessThread to stop
    cancellationTokenSource.Cancel();
    thread.Join();
}

/// The URI of the SpacetimeDB instance hosting our chat database and module.
const string HOST = "http://localhost:3000";

/// The database name we chose when we published our module.
const string DB_NAME = "cursor-chaos";

/// Load credentials from a file and connect to the database.
DbConnection ConnectToDB()
{
    DbConnection? conn = null;
    conn = DbConnection.Builder()
        .WithUri(HOST)
        .WithModuleName(DB_NAME)
        .WithToken(AuthToken.Token)
        .OnConnect(OnConnected)
        .OnConnectError(OnConnectError)
        .OnDisconnect(OnDisconnected)
        .Build();
    return conn;
}

/// `OnConnected` callback: save our credentials to a file.
void OnConnected(DbConnection conn, Identity identity, string authToken)
{
    local_identity = identity;
    AuthToken.SaveToken(authToken);

    conn.SubscriptionBuilder()
    .OnApplied(OnSubscriptionApplied)
    .SubscribeToAllTables();
}

/// `OnConnectError` callback: print the error, then exit the process.
void OnConnectError(Exception e)
{
    Console.Write($"Error while connecting: {e}");
}

/// `OnDisconnect` callback: print a note, then exit the process.
void OnDisconnected(DbConnection conn, Exception? e)
{
    if (e != null)
    {
        Console.Write($"Disconnected abnormally: {e}");
    }
    else
    {
        Console.Write($"Disconnected normally.");
    }
}

/// Register all the callbacks our app will use to respond to database events.
void RegisterCallbacks(DbConnection conn)
{
    conn.Db.User.OnInsert += User_OnInsert;
    conn.Db.User.OnUpdate += User_OnUpdate;

    conn.Db.Message.OnInsert += Message_OnInsert;

    conn.Reducers.OnSetName += Reducer_OnSetNameEvent;
    conn.Reducers.OnSendMessage += Reducer_OnSendMessageEvent;
}

/// If the user has no set name, use the first 8 characters from their identity.
string UserNameOrIdentity(User user) => user.Name ?? user.Identity.ToString()[..8];

/// Our `User.OnInsert` callback: if the user is online, print a notification.
void User_OnInsert(EventContext ctx, User insertedValue)
{
    if (insertedValue.Online)
    {
        Console.WriteLine($"{UserNameOrIdentity(insertedValue)} is online");
    }
}

/// `User.OnUpdate` callback:
/// print a notification about name and status changes.
void User_OnUpdate(EventContext ctx, User oldValue, User newValue)
{
    if (oldValue.Name != newValue.Name)
    {
        Console.WriteLine($"{UserNameOrIdentity(oldValue)} renamed to {newValue.Name}");
    }
    if (oldValue.Online != newValue.Online)
    {
        if (newValue.Online)
        {
            Console.WriteLine($"{UserNameOrIdentity(newValue)} connected.");
        }
        else
        {
            Console.WriteLine($"{UserNameOrIdentity(newValue)} disconnected.");
        }
    }
}

/// `Message.OnInsert` callback: print new messages.
void Message_OnInsert(EventContext ctx, Message insertedValue)
{
    // We are filtering out messages inserted during the subscription being applied,
    // since we will be printing those in the OnSubscriptionApplied callback,
    // where we will be able to first sort the messages before printing.
    if (ctx.Event is not Event<Reducer>.SubscribeApplied)
    {
        PrintMessage(ctx.Db, insertedValue);
    }
}

void PrintMessage(RemoteTables tables, Message message)
{
    var sender = tables.User.Identity.Find(message.Sender);
    var senderName = "unknown";
    if (sender != null)
    {
        senderName = UserNameOrIdentity(sender);
    }

    Console.WriteLine($"{senderName}: {message.Text}");
}

/// `OnSetNameEvent` callback: print a warning if the reducer failed to change the user's name.
void Reducer_OnSetNameEvent(ReducerEventContext ctx, string name)
{
    var e = ctx.Event;
    if (e.CallerIdentity == local_identity && e.Status is Status.Failed(var error))
    {
        Console.Write($"Failed to change name to {name}: {error}");
    }
}

/// `OnSendMessageEvent` callback: print a warning if the reducer failed to send the message.
void Reducer_OnSendMessageEvent(ReducerEventContext ctx, string text)
{
    var e = ctx.Event;
    if (e.CallerIdentity == local_identity && e.Status is Status.Failed(var error))
    {
        Console.Write($"Failed to send message {text}: {error}");
    }
}

/// `OnSubscriptionApplied` callback:
/// sort all past messages and print them in timestamp order.
void OnSubscriptionApplied(SubscriptionEventContext ctx)
{
    Console.WriteLine("Connected");
    PrintMessagesInOrder(ctx.Db);
}

void PrintMessagesInOrder(RemoteTables tables)
{
    foreach (Message message in tables.Message.Iter().OrderBy(item => item.Sent))
    {
        PrintMessage(tables, message);
    }
}

/// Our separate thread from main, where we can call process updates and process commands without blocking the main thread. 
void ProcessThread(DbConnection conn, CancellationToken ct)
{
    try
    {
        // loop until cancellation token
        while (!ct.IsCancellationRequested)
        {
            conn.FrameTick();

            ProcessCommands(conn.Reducers);

            Thread.Sleep(100);
        }
    }
    finally
    {
        conn.Disconnect();
    }
}

/// Read each line of standard input, and either set our name or send a message as appropriate.
void InputLoop()
{
    while (true)
    {
        var input = Console.ReadLine();
        if (input == null)
        {
            break;
        }

        if (input.StartsWith("/name "))
        {
            input_queue.Enqueue(("name", input[6..]));
            continue;
        }
        else
        {
            input_queue.Enqueue(("message", input));
        }
    }
}

void ProcessCommands(RemoteReducers reducers)
{
    // process input queue commands
    while (input_queue.TryDequeue(out var command))
    {
        switch (command.Command)
        {
            case "message":
                reducers.SendMessage(command.Args);
                break;
            case "name":
                reducers.SetName(command.Args);
                break;
        }
    }
}

Main();