let _logsUrl = "";
let _maxElements = 5000;
let logData = [];
let _connection = null;
var logsDiv;

var typingTimer;
var doneTypingInterval = 1000;

//Initialize variables
function Initialize(logsUrl, maxElements, logModel) {
	_logsUrl = logsUrl;
	_maxElements = maxElements;

	//Initialize the logData array
	logData = logModel.map(x => new LogEntry(x.datetime, x.type, x.message, x.sender));

	//Show how many messages are showing in the label
	$("#maxElements").text(`Showing the ${_maxElements} last logs`);
	logsDiv = $("#logsDiv");

	//Event when user types on search textbox. It uses a timer to avoid re-rendering at every letter typed
	$('#logsearch').on('input propertychange paste', function () {
		clearTimeout(typingTimer);
		typingTimer = setTimeout(applyFilter, doneTypingInterval);
	});

	$("#logsDate").on("change blur", function () {
		var inputValue = $(this).val();
		if (inputValue == null || inputValue === "")
			return;
		var newUrl = window.location.href.split("?")[0];
		var newDate = formatDate(new Date(inputValue));
		window.location = `${newUrl}?date=${newDate}`;
	});

	defineConnection();
	renderGrid(logData);

	var logsDate = new Date($("#logsDate").val());
	if (logsDate.toDateString() === new Date().toDateString())
		start();
	
}

function padTo2Digits(num) {
	return num.toString().padStart(2, '0');
}

function formatDate(date) {
	return [
		padTo2Digits(date.getDate()),
		padTo2Digits(date.getMonth() + 1),
		date.getFullYear(),
	].join('/');
}

//LogEntry class
function LogEntry(timestamp, level, message, sender) {
	this.timestamp = new Date(timestamp).toLocaleString();
	this.level = level;
	this.loglevel = getLogLevelNum(level);
	this.message = message;
	this.sender = sender;
	this.row = createLogElement(this.timestamp, this.level, this.message, this.sender);
}

//Function to apply the current filters
function applyFilter() {
	console.log("applyFilter");
	var logLevelNum = getLogLevelNum($("#loglevelfilter option:selected").val());
	var textToSearch = $("#logsearch").val().toLowerCase();
	var sender = $("#logsenderfilter option:selected").val();

	var filteredData = logData.filter(x => (logLevelNum == 0 || x.loglevel >= logLevelNum)
		&& (sender == "" || x.sender === sender)
		&& (textToSearch == "" || x.message.toLowerCase().includes(textToSearch)));

	renderGrid(filteredData);
}

//Cleans the grid
function cleanGrid() {
	logsDiv.find(".row").remove();
}

//Renders a single row in the grid
function renderRow(row) {
	logsDiv.prepend(row);
}

//Renders the grid with the LogEntry array passed by the parameter "data"
function renderGrid(data) {
	cleanGrid();
	var dataToDisplay = data.slice(-_maxElements);
	for (var i = 0; i < dataToDisplay.length; i++) {
		renderRow(dataToDisplay[i].row);
	}
}

//Function to map the Log Level string to a number to be filtered
function getLogLevelNum(logLevel) {
	switch (logLevel) {
		case "Trace":
			return 0;
		case "Debug":
			return 1;
		case "Information":
			return 2;
		case "Warning":
			return 3;
		case "Error":
			return 4;
	}
}

//Creates the row to draw
function createLogElement(timestamp, level, message, sender) {
	var row = $("<div class='row'></div>");
	var colTs = $("<div class='col-5 col-lg-3 col-xl-2 log-ts'></div>").text(timestamp)
		.addClass("timestamp");

	var colSender = $("<div class='col-4 col-lg-2 col-xl-2 log-sender'></div>").text(sender)
		.addClass("logsender");

	var colLevel = $("<div class='col-3 col-lg-2 col-xl-1 log-level'></div>").text(level);
	colLevel.addClass(level)
		.addClass("loglevel");

	var colMsg = $("<div class='col-12 col-lg-5 col-xl-7 log-msg'></div>").text(message)
		.addClass("logmessage");

	row.append(colTs)
		.append(colSender)
		.append(colLevel)
		.append(colMsg);

	return row;
}

//Function that is called for every log received by the hub
function renderLogEntry(timestamp, level, message, sender) {

	var logEntry = new LogEntry(timestamp, level, message, sender);
	logData.push(logEntry);

	//check if has to be drawn
	var logLevelSelected = getLogLevelNum($("#loglevelfilter option:selected").val());
	var render = true;

	console.log("LogLevelSelected: " + logLevelSelected + " . LogLevel: " + logEntry.loglevel);

	if (logLevelSelected !== 0 && logEntry.loglevel < logLevelSelected)
		render = false;

	var searchText = $("#logsearch").val();
	if (searchText !== "" && !message.toLowerCase().includes(searchText.toLowerCase()))
		render = false;

	var logSenderSelected = $("#logsenderfilter option:selected").val();
	if (logSenderSelected !== "" && sender !== logSenderSelected)
		render = false;

	if (render)
		renderRow(logEntry.row);

	//if we reached the maximum log messages, delete the last
	var elementsCount = logsDiv.find(".row").length;
	if (elementsCount > _maxElements) {
		var elementsToDelete = elementsCount - _maxElements;
		logsDiv.find(".row:nth-last-child(-n+" + elementsToDelete + ")").remove();
	}
}

function defineConnection() {
	//Definition of the connection to the SignalR hub that pushes the logs
	_connection = new signalR.HubConnectionBuilder()
		.withUrl(_logsUrl)
		.configureLogging(signalR.LogLevel.Information)
		.withAutomaticReconnect()
		.build();

	//If the connection closes unexpectedly, start again
	_connection.onclose(async () => {
		await start();
	});

	//Event that receives each message from the hub
	_connection.on("SendLogAsObject", (data) => {
		renderLogEntry(data.timestamp, data.level, data.message, data.LogSender);
	});

}
//Function to start the connection
async function start() {
	try {
		await _connection.start();
		console.log("SignalR Connected.");
	} catch (err) {
		console.log(err);
		setTimeout(start, 5000);
	}
};




