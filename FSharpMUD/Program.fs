open System
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open Newtonsoft.Json
open System.IO

// Define a type for storing user credentials
type User = {
    Username: string
    Password: string
}

// Define a type for a connected player
type Player = {
    Username: string
    Stream: NetworkStream
    mutable IsLoggedIn: bool
}

// MUD Server class to handle client connections, login, and chat
type MudServer(ipAddress: string, port: int) =
    // Create a TCP listener for the server
    let tcpListener = new TcpListener(IPAddress.Parse(ipAddress), port)
    // Load users from the JSON file
    let users =
        let usersJson = File.ReadAllText("users.json")  // Load JSON file containing user data
        JsonConvert.DeserializeObject<User list>(usersJson)  // Deserialize to list of User objects

    // List to store connected players
    let players = ref []

    // Function to send a message to all connected players
    let sendToAll message =
        players.Value |> List.iter (fun player ->
            let bytes = Encoding.ASCII.GetBytes(message + "\n")
            player.Stream.Write(bytes, 0, bytes.Length)
        )

    // Function to send a message to a specific player
    let sendToPlayer username message =
        match players.Value |> List.tryFind (fun player -> player.Username = username) with
        | Some(player) ->
            let bytes = Encoding.ASCII.GetBytes(message + "\n")
            player.Stream.Write(bytes, 0, bytes.Length)
        | None ->
            printfn "Player %s not found!" username

    // Function to handle communication with a single client
    member this.HandleClient (client: TcpClient) =
        async {
            use stream = client.GetStream()  // Get the network stream for communication
            let buffer = Array.create 1024 0uy  // Buffer to store incoming data

            // Ask for username
            let usernamePrompt = "Enter username: "
            let usernamePromptBytes = Encoding.ASCII.GetBytes(usernamePrompt)
            stream.Write(usernamePromptBytes, 0, usernamePromptBytes.Length)
            let! usernameBytesRead = Async.AwaitTask(stream.ReadAsync(buffer, 0, buffer.Length))
            let username = Encoding.ASCII.GetString(buffer, 0, usernameBytesRead).Trim()

            // Ask for password
            let passwordPrompt = "Enter password: "
            let passwordPromptBytes = Encoding.ASCII.GetBytes(passwordPrompt)
            stream.Write(passwordPromptBytes, 0, passwordPromptBytes.Length)
            let! passwordBytesRead = Async.AwaitTask(stream.ReadAsync(buffer, 0, buffer.Length))
            let password = Encoding.ASCII.GetString(buffer, 0, passwordBytesRead).Trim()

            // Check if the username and password match any in the user database
            match users |> List.tryFind (fun user -> user.Username = username && user.Password = password) with
            | Some(_) ->
                // Successful login
                let welcomeMessage = "Login successful! Welcome to F# MUD! Type 'help' for a list of commands."
                let welcomeBytes = Encoding.ASCII.GetBytes(welcomeMessage)
                stream.Write(welcomeBytes, 0, welcomeBytes.Length)
                printfn "User %s logged in successfully." username

                // Create a new player and add to the players list
                let player = { Username = username; Stream = stream; IsLoggedIn = true }
                players := player :: !players

                // Chat system loop
                let rec chatLoop () =
                    async {
                        let! bytesRead = Async.AwaitTask(stream.ReadAsync(buffer, 0, buffer.Length))
                        if bytesRead > 0 then
                            let input = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim()
                            if input.StartsWith("/say") then
                                // Broadcast message to all players
                                let message = input.Substring(5).Trim() // Remove "/say " from the start
                                sendToAll (username + " says: " + message)
                            elif input.StartsWith("/whisper") then
                                // Send a private message to a specific player
                                let parts = input.Split(' ', 3)
                                if parts.Length > 2 then
                                    let recipient = parts.[1]
                                    let message = parts.[2]
                                    sendToPlayer recipient (username + " whispers: " + message)
                                else
                                    let errorMsg = "Usage: /whisper <username> <message>"
                                    let errorBytes = Encoding.ASCII.GetBytes(errorMsg + "\n")
                                    stream.Write(errorBytes, 0, errorBytes.Length)
                            elif input = "/quit" then
                                // Handle disconnect
                                let quitMessage = "You have disconnected. Goodbye!"
                                let quitBytes = Encoding.ASCII.GetBytes(quitMessage + "\n")
                                stream.Write(quitBytes, 0, quitBytes.Length)
                                players := List.filter (fun p -> p.Username <> username) !players
                                client.Close()  // Close the connection
                            else
                                // Invalid command
                                let invalidCmdMsg = "Invalid command. Type '/say <message>' to chat or '/whisper <username> <message>' for private messages."
                                let invalidCmdBytes = Encoding.ASCII.GetBytes(invalidCmdMsg + "\n")
                                stream.Write(invalidCmdBytes, 0, invalidCmdBytes.Length)
                        do! chatLoop ()
                    }
                do! chatLoop ()
            | None ->
                // Login failed
                let failureMessage = "Login failed. Please try again."
                let failureBytes = Encoding.ASCII.GetBytes(failureMessage + "\n")
                stream.Write(failureBytes, 0, failureBytes.Length)
                printfn "Failed login attempt for %s." username
        }

    // Function to start the server
    member this.Start() =
        tcpListener.Start()  // Start listening for incoming connections
        printfn "MUD Server is running on %s:%d" ipAddress port
        while true do
            let client = tcpListener.AcceptTcpClient()  // Accept incoming client connection
            printfn "Client connected!"
            Async.Start(this.HandleClient(client))  // Handle the client asynchronously

// Entry point for the server application
[<EntryPoint>]
let main argv =
    let server = new MudServer("127.0.0.1", 23)  // Start the server on localhost and port 23
    server.Start()  // Start listening for clients
    0  // Exit code