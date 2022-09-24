using RestSharp;
using System;
using System.Collections.Generic;
using Tbot.Model;
using Tbot.Includes;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;


namespace Tbot.Services {
	class OgamedService {
		private string Url { get; set; }
		private RestClient Client { get; set; }
		private Process ogamedProcess { get; set; } = null;

		public OgamedService(Credentials credentials, string host = "127.0.0.1", int port = 8080, string captchaKey = "", ProxySettings proxySettings = null, string cookiesPath = "") {
			ExecuteOgamedExecutable(credentials, host, port, captchaKey, proxySettings, cookiesPath);
			Url = $"http://{host}:{port}";
			Client = new(Url) {
				Timeout = 86400000,
				ReadWriteTimeout = 86400000
			};
			if (credentials.BasicAuthUsername != "" && credentials.BasicAuthPassword != "") {
				Client.Authenticator = new RestSharp.Authenticators.HttpBasicAuthenticator(credentials.BasicAuthUsername, credentials.BasicAuthPassword);
			}
		}

		internal void ExecuteOgamedExecutable(Credentials credentials, string host = "localhost", int port = 8080, string captchaKey = "", ProxySettings proxySettings = null, string cookiesPath = "cookies.txt") {
			try {
				string args = $"--universe=\"{credentials.Universe}\" --username={credentials.Username} --password={credentials.Password} --language={credentials.Language} --auto-login=false --port={port} --host=0.0.0.0 --api-new-hostname=http://{host}:{port} --cookies-filename={cookiesPath}";
				if (captchaKey != "")
					args += $" --nja-api-key={captchaKey}";
				if (proxySettings.Enabled) {
					if (proxySettings.Type == "socks5" || proxySettings.Type == "http") {
						args += $" --proxy={proxySettings.Address}";
						args += $" --proxy-type={proxySettings.Type}";
						if (proxySettings.Username != "")
							args += $" --proxy-username={proxySettings.Username}";
						if (proxySettings.Password != "")
							args += $" --proxy-password={proxySettings.Password}";
						if (proxySettings.LoginOnly)
							args += " --proxy-login-only=true";
					}
				}
				if (credentials.IsLobbyPioneers)
					args += " --lobby=lobby-pioneers";
				if (credentials.BasicAuthUsername != "" && credentials.BasicAuthPassword != "") {
					args += $" --basic-auth-username={credentials.BasicAuthUsername}";
					args += $" --basic-auth-password={credentials.BasicAuthPassword}";
				}
				if (cookiesPath.Length > 0)
					args += $" --cookies-filename=\"{cookiesPath}\"";

				Process ogameProc = new Process();
				ogameProc.StartInfo.FileName = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? "ogamed.exe" : "ogamed";
				ogameProc.StartInfo.Arguments = args;
				ogameProc.EnableRaisingEvents = true;
				ogameProc.Exited += handle_ogamedProcess_Exited;

				ogameProc.Start();
				Helpers.WriteLog(LogType.Info, LogSender.OGameD, $"OgameD Started with PID {ogameProc.Id}");   // This would raise an exception
				ogamedProcess = ogameProc;
			} catch {
				Environment.Exit(0);
			}
		}

		private void handle_ogamedProcess_Exited(object sender, EventArgs e) {
			Helpers.WriteLog(LogType.Error, LogSender.OGameD, $"OgameD Exited {e.ToString()}");
			Environment.Exit(0);
		}

		public void KillOgamedExecultable() {
			if (ogamedProcess != null) {
				ogamedProcess.Close();
				ogamedProcess.Kill();
				ogamedProcess = null;
			}
		}

		public bool SetUserAgent(string userAgent) {
			try {
				var request = new RestRequest {
					Resource = "/bot/set-user-agent",
					Method = Method.POST,
				};
				request.AddParameter("userAgent", userAgent, ParameterType.GetOrPost);
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool Login() {
			try {
				var request = new RestRequest {
					Resource = "/bot/login",
					Method = Method.GET
				};

				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool Logout() {
			try {
				var request = new RestRequest {
					Resource = "/bot/logout",
					Method = Method.GET
				};

				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public string GetCaptchaChallengeID() {
			try {
				var request = new RestRequest {
					Resource = "/bot/captcha/challengeID",
					Method = Method.GET
				};

				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result != null && result.Status == "ok")
					return (string) result.Result;
				else
					return "";
			} catch { return ""; }
		}

		public byte[] GetCaptchaTextImage(string challengeID) {
			try {
				var request = new RestRequest {
					Resource = "/bot/captcha/question/" + challengeID,
					Method = Method.GET
				};

				var response = Client.Execute(request);
				var result = response.RawBytes;
				if (result != null)
					return result;
				else
					return new byte[0];
			} catch { return new byte[0]; }
		}

		public byte[] GetCaptchaIcons(string challengeID) {
			try {
				var request = new RestRequest {
					Resource = "/bot/captcha/icons/" + challengeID,
					Method = Method.GET
				};

				var response = Client.Execute(request);
				var result = response.RawBytes;
				if (result != null)
					return result;
				else
					return new byte[0];
			} catch { return new byte[0]; }
		}

		public void SolveCaptcha(string challengeID, int answer) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/captcha/solve",
					Method = Method.POST,
				};

				request.AddParameter("challenge_id", challengeID, ParameterType.GetOrPost);
				request.AddParameter("answer", answer, ParameterType.GetOrPost);
				Client.Execute(request);
			} catch { }
		}

		public string GetOgamedIP() {
			var request = new RestRequest {
				Resource = "/bot/ip",
				Method = Method.GET
			};

			try {
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok") {
					return "";
				} else
					return result.Result;
			} catch {
				return "";
			}
		}

		public string GetTbotIP() {
			var request = new RestRequest {
				Resource = "https://jsonip.com/",
				Method = Method.GET
			};
			try {
				var result = JsonConvert.DeserializeObject<dynamic>(Client.Execute(request).Content);
				return result.ip;
			} catch {
				return "";
			}
		}

		public Server GetServerInfo() {
			var request = new RestRequest {
				Resource = "/bot/server",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Server>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public ServerData GetServerData() {
			var request = new RestRequest {
				Resource = "/bot/server-data",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<ServerData>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public string GetServerUrl() {
			var request = new RestRequest {
				Resource = "/bot/server-url",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (string) result.Result;
		}

		public string GetServerLanguage() {
			var request = new RestRequest {
				Resource = "/bot/language",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (string) result.Result;
		}

		public string GetServerName() {
			var request = new RestRequest {
				Resource = "/bot/universe-name",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (string) result.Result;
		}

		public int GetServerSpeed() {
			var request = new RestRequest {
				Resource = "/bot/server/speed",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (int) result.Result;
		}

		public int GetServerFleetSpeed() {
			var request = new RestRequest {
				Resource = "/bot/server/speed-fleet",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (int) result.Result;
		}

		public string GetServerVersion() {
			var request = new RestRequest {
				Resource = "/bot/server/version",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (string) result.Result;
		}

		public DateTime GetServerTime() {
			var request = new RestRequest {
				Resource = "/bot/server/time",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (DateTime) result.Result;
		}

		public string GetUsername() {
			var request = new RestRequest {
				Resource = "/bot/username",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (string) result.Result;
		}

		public UserInfo GetUserInfo() {
			var request = new RestRequest {
				Resource = "/bot/user-infos",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<UserInfo>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public CharacterClass GetUserClass() {
			var request = new RestRequest {
				Resource = "/bot/character-class",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<CharacterClass>(JsonConvert.SerializeObject(result.Result));
		}

		public List<Planet> GetPlanets() {
			var request = new RestRequest {
				Resource = "/bot/planets",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<List<Planet>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Planet GetPlanet(Planet planet) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{planet.ID}",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Planet>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public List<Moon> GetMoons() {
			var request = new RestRequest {
				Resource = "/bot/moons",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<List<Moon>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Moon GetMoon(Moon moon) {
			var request = new RestRequest {
				Resource = $"/bot/moons/{moon.ID}",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Moon>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public List<Celestial> GetCelestials() {
			var planets = this.GetPlanets();
			var moons = this.GetMoons();
			List<Celestial> celestials = new();
			celestials.AddRange(planets);
			celestials.AddRange(moons);
			return celestials;
		}

		public Celestial GetCelestial(Celestial celestial) {
			if (celestial is Moon)
				return this.GetMoon(celestial as Moon);
			else if (celestial is Planet)
				return this.GetPlanet(celestial as Planet);
			else
				return celestial;
		}

		public Techs GetTechs(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/celestials/{celestial.ID}/techs",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Techs>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Model.Resources GetResources(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/resources",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Buildings GetBuildings(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/resources-buildings",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.Buildings>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public LFBuildings GetLFBuildings(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/lifeform-buildings",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				Console.WriteLine(result.Result.ToString());
				return JsonConvert.DeserializeObject<Model.LFBuildings>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public LFTechs GetLFTechs(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/lifeform-techs",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.LFTechs>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Facilities GetFacilities(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/facilities",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Facilities>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Defences GetDefences(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/defence",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Defences>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Ships GetShips(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/ships",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Ships>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public bool IsUnderAttack() {
			var request = new RestRequest {
				Resource = "/bot/is-under-attack",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public bool IsVacationMode() {
			var request = new RestRequest {
				Resource = "/bot/is-vacation-mode",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public bool HasCommander() {
			var request = new RestRequest {
				Resource = "/bot/has-commander",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public bool HasAdmiral() {
			var request = new RestRequest {
				Resource = "/bot/has-admiral",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public bool HasEngineer() {
			var request = new RestRequest {
				Resource = "/bot/has-engineer",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public bool HasGeologist() {
			var request = new RestRequest {
				Resource = "/bot/has-geologist",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public bool HasTechnocrat() {
			var request = new RestRequest {
				Resource = "/bot/has-technocrat",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return (bool) result.Result;
		}

		public Staff GetStaff() {
			try {
				return new() {
					Commander = HasCommander(),
					Admiral = HasAdmiral(),
					Engineer = HasEngineer(),
					Geologist = HasGeologist(),
					Technocrat = HasTechnocrat()
				};
			} catch {
				return new();
			}
		}

		public bool BuyOfferOfTheDay() {
			try {
				var request = new RestRequest {
					Resource = "/bot/buy-offer-of-the-day",
					Method = Method.GET
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public List<AttackerFleet> GetAttacks() {
			var request = new RestRequest {
				Resource = "/bot/attacks",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<List<AttackerFleet>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public List<Fleet> GetFleets() {
			var request = new RestRequest {
				Resource = "/bot/fleets",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<List<Fleet>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Slots GetSlots() {
			var request = new RestRequest {
				Resource = "/bot/fleets/slots",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Slots>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Researches GetResearches() {
			var request = new RestRequest {
				Resource = "/bot/get-research",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Researches>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public List<Production> GetProductions(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/production",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<List<Production>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public ResourceSettings GetResourceSettings(Planet planet) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{planet.ID}/resource-settings",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<ResourceSettings>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public ResourcesProduction GetResourcesProduction(Planet planet) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{planet.ID}/resources-details",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.ResourcesProduction>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Constructions GetConstructions(Celestial celestial) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{celestial.ID}/constructions",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Constructions>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public bool CancelConstruction(Celestial celestial) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/cancel-building",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool CancelResearch(Celestial celestial) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/cancel-research",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public Fleet SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Model.Resources payload) {
			var request = new RestRequest {
				Resource = $"/bot/planets/{origin.ID}/send-fleet",
				Method = Method.POST,
			};

			request.AddParameter("galaxy", destination.Galaxy, ParameterType.GetOrPost);
			request.AddParameter("system", destination.System, ParameterType.GetOrPost);
			request.AddParameter("position", destination.Position, ParameterType.GetOrPost);
			request.AddParameter("type", (int) destination.Type, ParameterType.GetOrPost);

			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					request.AddParameter("ships", (int) buildable + "," + prop.GetValue(ships, null), ParameterType.GetOrPost);
				}
			}

			request.AddParameter("mission", (int) mission, ParameterType.GetOrPost);

			request.AddParameter("speed", speed.ToString(), ParameterType.GetOrPost);

			request.AddParameter("metal", payload.Metal, ParameterType.GetOrPost);
			request.AddParameter("crystal", payload.Crystal, ParameterType.GetOrPost);
			request.AddParameter("deuterium", payload.Deuterium, ParameterType.GetOrPost);
			request.AddParameter("food", payload.Food, ParameterType.GetOrPost);

			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Fleet>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public bool CancelFleet(Fleet fleet) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/fleets/{fleet.ID}/cancel",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public GalaxyInfo GetGalaxyInfo(Coordinate coordinate) {
			var request = new RestRequest {
				Resource = $"/bot/galaxy-infos/{coordinate.Galaxy}/{coordinate.System}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<GalaxyInfo>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public GalaxyInfo GetGalaxyInfo(int galaxy, int system) {
			Coordinate coordinate = new() { Galaxy = galaxy, System = system };
			return this.GetGalaxyInfo(coordinate);
		}

		public bool BuildCancelable(Celestial celestial, Buildables buildable) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildCancelable(Celestial celestial, LFBuildables buildable) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildCancelable(Celestial celestial, LFTechno buildable) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildConstruction(Celestial celestial, Buildables buildable) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/building/{(int) buildable}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildTechnology(Celestial celestial, Buildables buildable) {
			try {
				var request = new RestRequest { Resource = $"/bot/planets/{celestial.ID}/build/technology/{(int) buildable}", Method = Method.POST };
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildMilitary(Celestial celestial, Buildables buildable, long quantity) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/production/{(int) buildable}/{quantity}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildShips(Celestial celestial, Buildables buildable, long quantity) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/ships/{(int) buildable}/{quantity}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool BuildDefences(Celestial celestial, Buildables buildable, long quantity) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/planets/{celestial.ID}/build/defence/{(int) buildable}/{quantity}",
					Method = Method.POST,
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public Model.Resources GetPrice(Buildables buildable, long levelOrQuantity) {
			var request = new RestRequest {
				Resource = $"/bot/price/{(int) buildable}/{levelOrQuantity}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Model.Resources GetPrice(LFBuildables buildable, long levelOrQuantity) {
			var request = new RestRequest {
				Resource = $"/bot/price/{(int) buildable}/{levelOrQuantity}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Model.Resources GetPrice(LFTechno buildable, long levelOrQuantity) {
			var request = new RestRequest {
				Resource = $"/bot/price/{(int) buildable}/{levelOrQuantity}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Model.LFBuildings GetRequirements(LFBuildables buildable) { //works but returns LFbuildings as ID, lazy to do the mapping ID -> LFbuilding class to be able to use properties reflection
			var request = new RestRequest {
				Resource = $"bot/requirements/{(int) buildable}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<Model.LFBuildings>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public bool SendMessage(int playerID, string message) {
			try {
				var request = new RestRequest {
					Resource = "/bot/send-message",
					Method = Method.POST,
				};
				request.AddParameter("playerID", playerID, ParameterType.GetOrPost);
				request.AddParameter("message", message, ParameterType.GetOrPost);
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool DeleteReport(int reportID) {
			try {
				var request = new RestRequest {
					Resource = $"/bot/delete-report/{reportID}",
					Method = Method.POST
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public bool DeleteAllEspionageReports() {
			try {
				var request = new RestRequest {
					Resource = "/bot/delete-all-espionage-reports",
					Method = Method.POST
				};
				var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
				if (result.Status != "ok")
					return false;
				else
					return true;
			} catch { return false; }
		}

		public List<Model.EspionageReportSummary> GetEspionageReports() {
			var request = new RestRequest {
				Resource = "/bot/espionage-report",
				Method = Method.GET
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<List<EspionageReportSummary>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Model.EspionageReport GetEspionageReport(Coordinate coordinate) {
			var request = new RestRequest {
				Resource = $"/bot/espionage-report/{coordinate.Galaxy}/{coordinate.System}/{coordinate.Position}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<EspionageReport>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public Model.EspionageReport GetEspionageReport(int msgId) {
			var request = new RestRequest {
				Resource = $"/bot/espionage-report/{msgId}",
				Method = Method.GET,
			};
			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			} else
				return JsonConvert.DeserializeObject<EspionageReport>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
		}

		public bool JumpGate(Celestial origin, Celestial destination, Ships ships) {
			var request = new RestRequest {
				Resource = $"/bot/moons/{origin.ID}/jump-gate",
				Method = Method.POST,
			};

			request.AddParameter("moonDestination", destination.ID, ParameterType.GetOrPost);

			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					request.AddParameter("ships", (int) buildable + "," + prop.GetValue(ships, null), ParameterType.GetOrPost);
				}
			}

			var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
			if (!result.Status.Equals("ok")) { return false; } else { return true; }

		}
	}
}
