let _logsUrl = "";
let _getLogsUrl = "";
let logData = [];
let _connection = null;
var logsDiv;
let _loadMoreButton;
var _maxElements = 100;
var typingTimer;
var doneTypingInterval = 1000;

//Initialize variables
function Initialize(logsUrl, getLogsUrl) {
	showLoading();
	_logsUrl = logsUrl;
	_getLogsUrl = getLogsUrl;

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

	var logsDate = new Date($("#logsDate").val());
	updateGrid(_getLogsUrl + getFilterParameters(), function () {
		if (logsDate.toDateString() === new Date().toDateString()) {
			defineConnection();
			start();
		}
	});
}

function updateGrid(url, callback) {
	showLoading();
	$.get(url,
		function (data) {
			logData = data.content.sort((a, b) => (a.position > b.position)).map(x => new LogEntry(x.datetime, x.type, x.message, x.sender));
			_maxElements = logData.length > 100 ? logData.length : 100;
			renderGrid(logData);
			if (callback)
				callback();
			manageVisibilityLoadMore(data.hasMoreData);
			if (data.content.length == 0) {
				logsDiv.prepend("<div class='row'><span>No logs to show.</span></div>")
			}
			hideLoading();
		}
	);
	
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
	this.date = new Date(timestamp);
	this.timestamp = this.date.toLocaleString();
	this.originalTimestamp = timestamp;
	this.level = level;
	this.loglevel = getLogLevelNum(level);
	this.message = message;
	this.sender = sender;
	this.row = createLogElement(this.timestamp, this.level, this.message, this.sender);
}

function getFilterParameters() {
	var logLevel = $("#loglevelfilter option:selected").val();
	var textToSearch = $("#logsearch").val().toLowerCase();
	var sender = $("#logsenderfilter option:selected").val();
	let filterParameters = `&search=${encodeURIComponent(textToSearch)}&logSender=${encodeURIComponent(sender)}&logLevel=${encodeURIComponent(logLevel)}`;
	return filterParameters;
}

//Function to apply the current filters
function applyFilter() {
	console.log("applyFilter");
	let urlWithFilters = _getLogsUrl + getFilterParameters();
	updateGrid(urlWithFilters);
}

//Cleans the grid
function cleanGrid() {
	logsDiv.find(".row").remove();
}

//Renders a single row in the grid
function renderRow(row) {
	row.insertBefore(_loadMoreButton);
}

function renderRowTop(row) {
	logsDiv.prepend(row);
}

//Renders the grid with the LogEntry array passed by the parameter "data"
function renderGrid(data) {
	cleanGrid();
	for (var i = 0; i < data.length; i++) {
		renderRowTop(data[i].row);
	}
	renderLoadMoreButton();
}

function renderLoadMoreButton() {
	if ($("#loadMore").length > 0)
		return;
	let loadMoreButton = $("<button id='loadMore' class='btn btn-light' style='margin:10px;' onclick='loadMore()'>Load more logs</button>");
	let loadMoreButtonWrapper = $("<div style='width: 100%; text-align: center'></div>");
	loadMoreButton.appendTo(loadMoreButtonWrapper);
	logsDiv.append(loadMoreButtonWrapper);
	_loadMoreButton = loadMoreButtonWrapper;
}

function showLoading() {
	$("#backdrop").show();
}

function hideLoading() {
	$("#backdrop").hide();
}

function manageVisibilityLoadMore(show) {
	if (show) {
		_loadMoreButton.show();
	}
	else {

		_loadMoreButton.hide();
	}
}

function loadMore() {
	var lastItem = $(".logEntry").last();
	let timestamp = lastItem.find(".timestamp").text();
	let url = _getLogsUrl + "&lastTime=" + encodeURIComponent(timestamp) + getFilterParameters();
	showLoading();
	$.get(url,
		function (data) {
			let logEntries = data.content.sort((a, b) => (a.position < b.position)).map(x => new LogEntry(x.datetime, x.type, x.message, x.sender));
			_maxElements += logEntries.length;
			for (let i = 0; i < logEntries.length; i++) {
				logData.unshift(logEntries[i]);
				renderRow(logEntries[i].row);
			}
			manageVisibilityLoadMore(data.hasMoreData);
			hideLoading();
		});
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
	var row = $("<div class='row logEntry'></div>");
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
		renderRowTop(logEntry.row);

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




