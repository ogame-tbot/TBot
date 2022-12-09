let selectedFile = "";
let editor;
let _getFileUrl = "";
let _saveFileUrl = "";

function Initialize(getFileUrl, saveFileUrl) {
	_getFileUrl = getFileUrl;
	_saveFileUrl = saveFileUrl;

	editor = new JsonEditor('#json-display');
	let firstFile = $(".list-files li:first button");
	onClickFileLink(firstFile, firstFile.data("filename"));
}

function getJson(jsonContents) {
	try {
		return JSON.parse(jsonContents);
	} catch (ex) {
		alert('Wrong JSON Format: ' + ex);
	}
}

function openFileContents(fileName) {
	selectedFile = fileName;
	var url = `${_getFileUrl}?fileName=${fileName}`;
	$.get(url, function (response) {
		var content = getJson(response.data);
		editor.load(content);
	});
}

function onClickFileLink(link, fileName) {
	$(".list-files").find("button").removeClass("selected");
	$(link).addClass("selected");
	openFileContents(fileName);
}

function onReloadClick() {
	openFileContents(selectedFile);
}

function saveFileContents() {
	try {
		let fileName = selectedFile;
		let content = editor.get();
		let url = _saveFileUrl;
		$.post(url, { fileName: fileName, content: JSON.stringify(content) }, function (response) {
			if (!response.success) {
				alert(response.error);
			}
			else {
				alert("File saved successfully!");
			}
		});
	}
	catch (ex) {
		alert(ex);
	}
}
