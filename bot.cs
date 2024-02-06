using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.SqlClient;

public class Bot(string token)
{
    private readonly TelegramBotClient botClient = new(token);
    private long chatId;
    private string connectionString = "YOUR CONNECTION STRING HERE";
    
    //private string selectedName;
    //private string selectedColour;
    //private float selectedHours;
   // private string selectedWorkType;
    private int selectedHoursIsPaid;

    Dictionary<long, SetupState> chatStates = new Dictionary<long, SetupState>();
    Dictionary<long, List<string>> chatInfo = new Dictionary<long, List<string>>();
    enum SetupState
{
    AwaitingName,
    AwaitingColour,
    AwaitingNewSetup,
    awaitingWorkType,
    awaitingWorkHours,
    awaitingHoursIsPaid,
    awaitingCheckHours,
    awaitingConfirmHours,
    Completed
};

//keyboard markup
private ReplyKeyboardMarkup ColourKeyboard = new ReplyKeyboardMarkup(new[]
{
    new[] // first row
    {
        new KeyboardButton("Blue")
    }, 
    new [] 
    {
        new KeyboardButton("Red")
    },
    new [] 
    {
        new KeyboardButton("Green")
    }, 
    new []
    {
        new KeyboardButton("Orange")
    }
})
{
    ResizeKeyboard = true,
    OneTimeKeyboard = true
};

private ReplyKeyboardMarkup YesNoKeyboard = new ReplyKeyboardMarkup(new[]
{
    new[] // first row
    {
        new KeyboardButton("Ja")
    }, 
    new [] 
    {
        new KeyboardButton("Nein")
    }
})
{
    ResizeKeyboard = true,
    OneTimeKeyboard = true
};

private ReplyKeyboardMarkup NumericKeyboard = new ReplyKeyboardMarkup(new[]
{
    new KeyboardButton[] {"0.5", "1", "1.5"},
    ["2", "2.5", "3"],
    ["3.5", "4", "4.5"],
    ["5", "5.5", "6"]
})
{
    ResizeKeyboard = true, 
    OneTimeKeyboard = true 
};

private ReplyKeyboardMarkup WorkTypeKeyboard = new ReplyKeyboardMarkup(new[]
{
    new KeyboardButton[] {"Jungschar/Trainings (Fußball, Volleyball ...)"},
    ["Eventgruppe"],
    ["AE in Exter/Rehwinkel/etc."],
    ["Nebenjobs (OMS, BBS, etc.)"],
    ["Überstunden (Ateam)"],
    ["Private Projekte (Garten, Putzen, Nachhilfe, etc.)"],
    ["Lernwerkstatt"],
    ["Media/Musik/Buchhaltung/etc."],
    ["Ehrenamtlich überregional (DCG-Projekte, etc.)"],
    ["Trainer/Betreuer (Jungshcar/Mysports/etc.)"],
    ["Private bezahlte Projekte"],
    ["Ehranamtlich lokal (Musik/Media/übersetzung/etc.)"],
    ["Sonstiges" ]
})
{
    ResizeKeyboard = true,  // Recommended to fit the keyboard to the screen size
    OneTimeKeyboard = true  // Optional: hides the keyboard after a button is pressed
};


    public async void Start() {

        using CancellationTokenSource cts = new CancellationTokenSource();

        botClient.StartReceiving(
            updateHandler: HandleUpdate, 
            pollingErrorHandler: HandlePollingError,
            receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
            );
            
            //hello world from the bot
            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Hello, World! I am user {me.Id} and my name is {me.FirstName}.");

            var updates = await botClient.GetUpdatesAsync(offset: -1);
            if (updates.Any())
            {
                Console.WriteLine($"Received {updates.Count()} updates");
                var highestUpdateId = updates.Max(update => update.Id);
                // Confirm the updates are cleared by calling getUpdates again with the highest update_id + 1
                await botClient.GetUpdatesAsync(offset: highestUpdateId + 1);
            }
            
    }

    public async Task HandleUpdate(ITelegramBotClient client, Update update, CancellationToken token)
    {
        string selectedName = "null";
        string selectedColour = "null";
        if (update.Type == UpdateType.Message)
        {
            // if  (update.Message.Chat.Id < 0)
            // {
            //     return;
            // }

            var message = update.Message;
            chatId = message.Chat.Id;
            Console.WriteLine("update received from " + chatId);

            //check if the client is already registered
            if (!chatStates.ContainsKey(chatId))
            {
                (string Name, string Colour) checkClientId = await GetClientInfoAsync(chatId);
                if (checkClientId.Name != null)
                {
                    selectedName = checkClientId.Name;
                    selectedColour = checkClientId.Colour;
                    chatStates.Add(chatId, SetupState.Completed);
                    try {
                    await botClient.SendTextMessageAsync(chatId: chatId, 
                            text: "Willkommen zurück, Sie sind derzeit als " + 
                            selectedName + " im Team " + selectedColour +   
                            " Registriert. Geben Sie /starten ein, um den Benutzer zu wechseln, oder /stundenregistrieren, um Stunden zu registrieren.");
                    }
                    catch (ApiRequestException apiEx) when (apiEx.ErrorCode == 403)
                        {
                            Console.WriteLine("User has blocked the bot. Moving on.");
                            // Log the error or take specific actions if necessary.
                            // Crucially, don't rethrow the exception; just continue.
                        }
                }
                else
                {
                    await HandleSetupCommand(message.Chat.Id, selectedName, selectedColour);
                }
            }
            else 
            {
                switch (message?.Type)
                {
                    case MessageType.Text:
                        await HandleTextMessage(message);
                        break;
                    // Handle other message types like photos, stickers, etc.
                    default:
                        await client.SendTextMessageAsync(chatId: chatId, "Entschuldigung, ich verstehe diese Art von Nachricht nicht. Versuche mir eine Textnachricht zu senden.");
                        break;
                }
            }       
        }
    }

    public async Task HandleTextMessage(Message message)
    {
        var clientInfo = await GetClientInfoAsync(chatId);
        string selectedName = clientInfo.Name;
        string selectedColour = clientInfo.Colour;
        

        if (message.Text.StartsWith("/"))
        {
            await HandleCommands(message, selectedName, selectedColour);
        }
        else 
        {
            switch (chatStates[chatId])
            {
                case SetupState.AwaitingName:
                    selectedName = message.Text;
                    await AddUserAsync(selectedName.ToUpper(), "NULL");
                    await addClientAsync(chatId, selectedName.ToUpper());
                    chatStates[chatId] = SetupState.AwaitingColour;
                    await botClient.SendTextMessageAsync(chatId: chatId, 
                        text: "Bitte wähle deine Teamfarbe:", 
                        replyMarkup: ColourKeyboard);
                    break;

                case SetupState.AwaitingColour:
                    // After receiving the input
                    selectedColour = message.Text;
                    chatStates[chatId] = SetupState.Completed;
                    await AddUserAsync(selectedName.ToUpper(), selectedColour.ToUpper());
                    
                    await botClient.SendTextMessageAsync(chatId: chatId, 
                        text: "Die Einrichtung ist abgeschlossen! Du bist jetzt bereit, Stunden für " + 
                        selectedName + " im Team " + selectedColour + 
                        " zu registrieren. Tippe /stundenregistrieren, um mit der Erfassung der Stunden zu beginnen.");
                    break;

                case SetupState.AwaitingNewSetup:
                    if (message.Text.ToLower() == "ja" || message.Text.ToLower() == "yes")
                    {
                        chatStates[chatId] = SetupState.AwaitingName;
                        await botClient.SendTextMessageAsync(chatId: chatId, 
                            text: "Bitte gib den neuen Vor- und Nachnamen ein: \n(zum Beispiel: John Smith)");
                    }
                    else
                    {
                        chatStates[chatId] = SetupState.Completed;
                        await botClient.SendTextMessageAsync(chatId: chatId, 
                                text: "Ok, wir werden die Stundenerfassung für " + selectedName + " fortsetzen. Alles andere bleibt unverändert.");
                    }
                    break;

                case SetupState.awaitingWorkHours:
                    bool success = float.TryParse(message.Text, out float selectedHours);
                    if (!success)
                    {
                        await botClient.SendTextMessageAsync(chatId: chatId, 
                            text: "Entschuldigung, ich verstehe diese Nummer nicht, bitte versuche es noch einmal.");
                    }
                    else
                    {
                        chatInfo.Add(chatId, [selectedHours.ToString()]);
                        chatStates[chatId] = SetupState.awaitingWorkType;
                        await botClient.SendTextMessageAsync(chatId: chatId, 
                            text: "Was für eine Arbeit hast du gemacht?",
                            replyMarkup: WorkTypeKeyboard);
                    }
                    break;

                case SetupState.awaitingWorkType:
                    chatInfo[chatId].Add(message.Text);
                    //string selectedWorkType = message.Text;
                    chatStates[chatId] = SetupState.awaitingHoursIsPaid;
                    await botClient.SendTextMessageAsync(chatId: chatId, 
                        text: "Wurdest du für diese Arbeit bezahlt?",
                        replyMarkup: YesNoKeyboard);
                    break;

                case SetupState.awaitingHoursIsPaid:
                    string isPaid;
                    if (message.Text.ToLower() == "ja" || message.Text.ToLower() == "yes")
                    {
                        selectedHoursIsPaid = 1;
                        isPaid = "bezahlt";
                        chatInfo[chatId].Add("1");
                    }
                    else
                    {
                        selectedHoursIsPaid = 0;
                        isPaid = "unbezahlt";
                        chatInfo[chatId].Add("0");
                    }
                    chatStates[chatId] = SetupState.awaitingCheckHours;
                    await botClient.SendTextMessageAsync(chatId: chatId, 
                    text: "Du bist dabei, " + chatInfo[chatId][0] + " " + isPaid + 
                    " Stunden für " + selectedName + " im Team " + selectedColour + 
                    " für " + chatInfo[chatId][1] + " zu registrieren. \nIst das korrekt?",
                    replyMarkup: YesNoKeyboard);
                    break;
                case SetupState.awaitingCheckHours:
                    if (message.Text.ToLower() == "ja" || message.Text.ToLower() == "yes")
                    {
                        chatStates[chatId] = SetupState.Completed;
                        await botClient.SendTextMessageAsync(chatId: chatId, 
                        text: "Nice! Ich werde " + chatInfo[chatId][0] + " Stunden für " + 
                        selectedName + " im Team " + selectedColour + " für " + chatInfo[chatId][1] + 
                        " registrieren. \nTippe /stundenregistrieren, um weitere Stunden zu registrieren, oder /starten, um den Benutzer zu wechseln.");
                        selectedHours = float.Parse(chatInfo[chatId][0]);
                        selectedHoursIsPaid = int.Parse(chatInfo[chatId][2]);
                        await RegisterHoursAsync(chatId, selectedName, chatInfo[chatId][1], selectedHours, selectedHoursIsPaid);
                        chatInfo.Remove(chatId);
                    }
                    else
                    {
                        chatStates[chatId] = SetupState.Completed;
                        await botClient.SendTextMessageAsync(chatId: chatId, 
                        text: "Die Stunden wurden nicht registriert, bitte tippe /stundenregistrieren, um von vorne zu beginnen.");
                        chatInfo.Remove(chatId);
                    }
                    break;

                default:
                    await botClient.SendTextMessageAsync(chatId: chatId, text: "Entschuldigung, ich verstehe diesen Befehl nicht. Bitte versuche es erneut.");
                    break;
            }
            
        }
       
    }

    public async Task HandleCommands(Message message, string selectedName, string selectedColour)
    {
        switch (message.Text)
        {
            case "/starten":
                await HandleSetupCommand(message.Chat.Id, selectedName, selectedColour);
                break;
            case "/stundenregistrieren":
                await HandleRegisterHoursCommand(message);
                break;
            // case "/stundenpruefen":
            //     float totalHours = await GetTotalRegisteredHoursAsync(selectedName);
            //     await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Du hast bisher " + totalHours + " Stunden registriert.");
            //     break;
            default:
                await botClient.SendTextMessageAsync(chatId: message.Chat.Id, text: "Entschuldigung, ich verstehe diesen Befehl nicht.");
                break;
        }
    }

    public async Task HandleSetupCommand(long chatId, string selectedName, string selectedColour)
    {
        if (chatStates.ContainsKey(chatId)) 
        {
            await botClient.SendTextMessageAsync(chatId: chatId, 
            text: $"Du bist gerade dabei, Stunden für {selectedName} zu registrieren, der/die im Team {selectedColour} ist. Möchtest du zu einem anderen Benutzer wechseln?",
            replyMarkup: YesNoKeyboard);
            chatStates[chatId] = SetupState.AwaitingNewSetup;
        }
        else 
        {
            chatStates[chatId] = SetupState.AwaitingName;
            await botClient.SendTextMessageAsync(chatId: chatId, text: "Willkommen! Bitte gib deinen Vor- und Nachnamen ein: \n (zum Beispiel: John Smith)");
        }
    }

    public async Task HandleRegisterHoursCommand(Message message)
    {
        await botClient.SendTextMessageAsync(chatId: chatId, 
                text: "Wie viele Stunden möchtest du registrieren?",
                replyMarkup: NumericKeyboard);
        chatStates[chatId] = SetupState.awaitingWorkHours;
    }

    //SQL methods
    public async Task AddUserAsync(string name, string color)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = @"
                IF EXISTS (SELECT 1 FROM USERS WHERE Name = @Name)
                BEGIN
                    UPDATE USERS SET Colour = @Colour WHERE Name = @Name
                END
                ELSE
                BEGIN
                INSERT INTO USERS (Name, Colour) VALUES (@Name, @Colour)
                END";

            Console.WriteLine("inserting " + name.Trim() + " " + color + " into database");

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Name", name.Trim());
                command.Parameters.AddWithValue("@Colour", color);

                connection.Open();
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task addClientAsync(long chatId, string name)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = @"
                IF EXISTS (SELECT 1 FROM CLIENT WHERE CLIENTID = @CLIENTID)
                BEGIN
                    UPDATE CLIENT SET NAME = @NAME WHERE CLIENTID = @CLIENTID
                END
                ELSE
                BEGIN
                INSERT INTO CLIENT (CLIENTID, NAME) VALUES (@CLIENTID, @NAME)
                END";

            Console.WriteLine("inserting " + chatId + " " + name.Trim() + " into database");

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@CLIENTID", chatId);
                command.Parameters.AddWithValue("@NAME", name.Trim().ToUpper());

                connection.Open();
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task<(string Name, string Colour)> GetClientInfoAsync(long clientId)
    {
        string name = null;
        string colour = null;

        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = @"
                IF EXISTS (SELECT 1 FROM CLIENT WHERE CLIENTID = @CLIENTID)
                BEGIN
                    SELECT USERS.NAME, USERS.COLOUR 
                    FROM USERS 
                    INNER JOIN CLIENT ON USERS.NAME = CLIENT.NAME 
                    WHERE CLIENT.CLIENTID = @CLIENTID
                END";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@CLIENTID", clientId);

                connection.Open();

                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        name = reader.GetString(0); // Get the name from the first column
                        colour = reader.GetString(1); // Get the colour from the second column
                    }
                }
            }
        }

        return (name, colour);
    }


    public async Task RegisterHoursAsync(long chatID, string selectedName, string selectedWorkType, float selectedHours, int selectedHoursIsPaid)
    {
        var clientInfo = await GetClientInfoAsync(chatID);
        selectedName = clientInfo.Name;
        var datetime = DateTime.Now;
        datetime.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                string query = @"
                    INSERT INTO REGISTEREDHOURS (NAME, WORKTYPE, HOURS, HOURSPAID, DATE)
                    VALUES (@Name, @WorkType, @Hours, @HoursPaid, @Date)";

                Console.WriteLine("inserting " + selectedName + " " + selectedWorkType + " " + selectedHours + " " + selectedHoursIsPaid + " into database");

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Name", selectedName);
                    command.Parameters.AddWithValue("@WorkType", selectedWorkType);
                    command.Parameters.AddWithValue("@Hours", selectedHours);
                    command.Parameters.AddWithValue("@HoursPaid", selectedHoursIsPaid);
                    command.Parameters.AddWithValue("@Date", datetime);

                    connection.Open();
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        catch (SqlException ex)
        {
            // Handle any SQL exceptions here
            Console.WriteLine("SQL Error: " + ex.Message);
        }
        catch (Exception ex)
        {
            // Handle any other exceptions here
            Console.WriteLine("General Error: " + ex.Message);
        }
    }

    // public async Task<float> GetTotalRegisteredHoursAsync(string name)
    // {
    //     float totalHours = 0;

    //     using (SqlConnection connection = new SqlConnection(connectionString))
    //     {
    //         string query = @"
    //             SELECT SUM(HOURS)
    //             FROM REGISTEREDHOURS 
    //             WHERE NAME = @Name";

    //         using (SqlCommand command = new SqlCommand(query, connection))
    //         {
    //             command.Parameters.AddWithValue("@Name", name);

    //             connection.Open();

    //             object result = await command.ExecuteScalarAsync();
    //             if (result != DBNull.Value)
    //             {
    //                 totalHours = (float)(double)result;
    //             }
    //         }
    //     }

    //     return totalHours;
    // }

    //exception handling
    private Task HandlePollingError(ITelegramBotClient client, Exception exception, CancellationToken token)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine("here is the error: " + ErrorMessage);
        return Task.CompletedTask;
    }
}