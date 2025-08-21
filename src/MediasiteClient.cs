using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using MediasiteUtil.Models;
using RestSharp;

namespace MediasiteUtil
{
	/// <summary>
	/// Wrapper for Mediasite 7 REST API calls.
	/// </summary>
	public partial class MediasiteClient
	{
		private MediasiteConfig _config;
		private string _errorMessage;
		private static string _message;
		private RestClient _client;
		private const int _batchSize = 10;	// batch size for paginated queries

		/// <summary>
		/// Default constructor
		/// </summary>
		public MediasiteClient()
		{
		}

		/// <summary>
		/// Instantiate a MediasiteUtil object with endpoint, username, password, and apiKey
		/// </summary>
		/// <param name="endpoint">URL of Mediasite REST API</param>
		/// <param name="username">Username for API authentication</param>
		/// <param name="password">Password for API authentication</param>
		/// <param name="apiKey">API Key for authentication</param>
		public MediasiteClient(string endpoint, string username, string password, string apiKey)
		{
			Config = new MediasiteConfig
			{
			    Endpoint = endpoint,
			    Username = username,
			    Password = password,
			    ApiKey = apiKey
			};
		}

		/// <summary>
		/// Instantiate a MediasiteUtil object with endpoint, username, password, apiKey, and startFolderId
		/// </summary>
		/// <param name="endpoint">URL of Mediasite REST API</param>
		/// <param name="username">Username for API authentication</param>
		/// <param name="password">Password for API authentication</param>
		/// <param name="apiKey">API Key for authentication</param>
		/// <param name="startFolderId">Resource Id of starting folder</param>
		public MediasiteClient(string endpoint, string username, string password, string apiKey, string startFolderId)
		{
			Config = new MediasiteConfig
			{
				Endpoint = endpoint,
				Username = username,
				Password = password,
				ApiKey = apiKey,
				RootFolderId = startFolderId
			};
		}

		public MediasiteConfig Config
		{
			set
			{
				if (_config != null)
				{
					throw new Exception("Mediasite Client is alrady configured.");
				}
				_config = value;
			}
		}

		public RestClient Client
		{
			get
			{
				_errorMessage = null;
				_message = null;

				if (_client == null)
				{
					var options = new RestClientOptions(_config.Endpoint)
					{
						Authenticator = new Auth(_config.Username, _config.Password, _config.ApiKey)
                    };
					_client = new RestClient(options);
					_client.AddDefaultHeader("sfapikey", _config.ApiKey);
				}
				return _client;
			}
		}

		/// <summary>
		/// Returns error message from last client call
		/// </summary>
		public string ErrorMessage
		{
			get
			{
				return _errorMessage;
			}
		}

		/// <summary>
		/// Returns info message from last client call
		/// </summary>
		public string Message
		{
			get
			{
				return _message;
			}
		}

		/// <summary>
		/// Returns error status of last call
		/// </summary>
		public bool HasError
		{
			get
			{
				return ErrorMessage != null;
			}
		}

		/// <summary>
		/// Finds and returns a folder by folder name, exact match
		/// </summary>
		/// <param name="folderName"></param>
		/// <returns></returns>
		public Folder FindFolder(string folderName)
		{
			// build a filter to find the folder
			var filter = String.Format("Name eq '{0}'", folderName);
			return GetFolderWithFilter(filter);
		}

		/// <summary>
		/// Finds and returns a folder by folder name, exact match.  Searches in a parent folder.
		/// </summary>
		/// <param name="folderName"></param>
		/// <param name="parentFolderId"></param>
		/// <returns></returns>
		public Folder FindFolder(string folderName, string parentFolderId)
		{
			// build a filter to find the folder
			var filter = String.Format("Name eq '{0}' and ParentFolderId eq '{1}'", folderName, parentFolderId);
			return GetFolderWithFilter(filter);
		}

		/// <summary>
		/// Finds and returns a folder by folder name, exact match
		/// </summary>
		/// <param name="folderId"></param>
		/// <returns></returns>
		public Folder FindFolderById(string folderId)
		{
			// request the folder
			var resource = String.Format("Folders('{0}')", folderId);
			var request = new RestRequest(resource, Method.Get);
			var folder = Client.Execute<Folder>(request);

			// check for errors and expected number of results
			ExpectResponse(HttpStatusCode.OK, request, folder);
			return folder.Data;
		}

		/// <summary>
		/// Finds all folders contained in the given parentFolderId, top level only
		/// </summary>
		/// <param name="parentFolderId"></param>
		/// <returns>A list of folders</returns>
		public List<Folder> FindFolders(string parentFolderId)
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// request the folder
			var filter = String.Format("ParentFolderId eq '{0}'", parentFolderId);
			var folders = new List<Folder>();
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest("Folders", filter, "Name", _batchSize, current);
				var results = Client.Execute<OData<List<Folder>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				folders.AddRange(results.Data.Value);
			}

			return folders;
		}

		/// <summary>
		/// Finds all folders starting with a given string, in the specified parent folder.
		/// </summary>
		/// <param name="startsWith"></param>
		/// <param name="parentFolderId"></param>
		/// <returns></returns>
		/// <remarks>$filter=startswith(CompanyName, 'Alfr')</remarks>
		public List<Folder> FindFoldersStartingWith(string startsWith, string parentFolderId)
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// request the folder
			var filter = String.Format("ParentFolderId eq '{0}' and startswith(Name, '{1}')", parentFolderId, startsWith);
			var folders = new List<Folder>();
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest("Folders", filter, "Name", _batchSize, current);
				var results = Client.Execute<OData<List<Folder>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				folders.AddRange(results.Data.Value);
			}

			return folders;
		}

		/// <summary>
		/// Finds all folders contained in the given parent folder, including all subfolders if specified.
		/// </summary>
		/// <param name="parentFolderId"></param>
		/// <param name="includeSubfolders"></param>
		/// <param name="includeParentFolder"></param>
		/// <returns>A non-hierarchical list of Folders</returns>
		public List<Folder> FindFolders(string parentFolderId, bool includeSubfolders, bool includeParentFolder)
		{
			var foundFolders = FindFolders(parentFolderId);

			if (!includeSubfolders || foundFolders.Count == 0)
			{
				return foundFolders;
			}

			// recurse through subfolders of each folder returned
			foreach (var folder in foundFolders)
			{
				folder.ChildFolders = FindFolders(folder.Id, true, false);
			}

			if (includeParentFolder)
			{
				var parentFolder = FindFolderById(parentFolderId);
				parentFolder.ChildFolders = foundFolders;
				return new List<Folder> { parentFolder };
			}
			return foundFolders;
		}

		/// <summary>
		/// Finds and returns a catalog by catalog name, exact match.
		/// </summary>
		/// <param name="catalogName"></param>
		/// <returns></returns>
		public Catalog FindCatalog(string catalogName)
		{
			var request = new RestRequest("Catalogs", Method.Get);
			request.AddParameter("$filter", String.Format("Name eq '{0}'", catalogName));
			request.RootElement = "value";
			var catalogs = Client.Execute<List<Catalog>>(request);

			// check for errors and expected number of results
			ExpectResponse(HttpStatusCode.OK, request, catalogs);
            // filter for exact match, since Mediasite Search service will return all results
            // where searchterm appears anywhere in the Title.
            catalogs.Data = catalogs.Data.Where(c => c.Name == catalogName).ToList();
			ExpectSingleResult(request, catalogs.Data);

			return catalogs.Data[0];
		}

		/// <summary>
		/// Gets all Players
		/// </summary>
		/// <returns></returns>
		public List<Player> GetPlayers()
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// get all players - paged query
			var players = new List<Player>();
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest("Players", "", "Name", _batchSize, current);
				var results = Client.Execute<OData<List<Player>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				players.AddRange(results.Data.Value);
			}

			return players;
		}

		#region Presentations
		/// <summary>
		/// Gets Presentation by Id
		/// </summary>
		/// <param name="presentationId"></param>
		/// <returns></returns>
		public PresentationFullRepresentation GetPresentationById(string presentationId)
		{
			// request the presentation
			var resource = String.Format("Presentations('{0}')", presentationId);
			var request = new RestRequest(resource, Method.Get);
			request.AddParameter("$select", "full");
			var presentation = Client.Execute<PresentationFullRepresentation>(request);
			
			// check for errors and expected number of results
			ExpectResponse(HttpStatusCode.OK, request, presentation);
			return presentation.Data;
		}

		/// <summary>
		/// Finds and returns all presentations in a given folder
		/// </summary>
		/// <param name="parentFolderId"></param>
		/// <returns></returns>
		public List<PresentationFullRepresentation> FindPresentationsInFolder(string parentFolderId)
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// find all presentations in the specified folder
			var filter = String.Format("ParentFolderId eq '{0}'", parentFolderId);
			var presentations = new List<PresentationFullRepresentation>();
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest("Presentations", filter, "Title", _batchSize, current);
				request.AddParameter("$select", "full");		// "card" returns less than "full" but doesn't include Parent Folder ID
				var results = Client.Execute<OData<List<PresentationFullRepresentation>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				presentations.AddRange(results.Data.Value);
			}

			return presentations;
		}

		/// <summary>
		/// Creates a Presentation
		/// </summary>
		/// <param name="p"></param>
		/// <param name="SelectedRecorder"></param>
		public PresentationFullRepresentation CreatePresentation(Recorder recorder, Template template, String folderId, String playerId, String participants)
		{
			var request = new RestRequest(String.Format("Templates('{0}')/CreatePresentationFromTemplate", template.Id), Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(NewPresentationJson(folderId, playerId, participants));
			var results = Client.Execute<PresentationFullRepresentation>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			return results.Data;
		}

		/// <summary>
		/// Creates a Presentation
		/// </summary>
		/// <param name="p"></param>
		/// <param name="SelectedRecorder"></param>
		public PresentationFullRepresentation CreatePresentation(Template template, String folderId, String playerId, String title, DateTime recordTime)
		{
			var request = new RestRequest(String.Format("Templates('{0}')/CreatePresentationFromTemplate", template.Id), Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(NewPresentationWithTitle(folderId, playerId, title, recordTime));
			var results = Client.Execute<PresentationFullRepresentation>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			return results.Data;
		}

		/// <summary>
		/// Create object for new presentation in Interview Room
		/// </summary>
		/// <param name="folderId"></param>
		/// <param name="playerId"></param>
		/// <param name="participants"></param>
		/// <returns></returns>
		private object NewPresentationJson(string folderId, string playerId, string participants)
		{
			return new
			{
				Title = String.Format("{0} - {1}", DateTime.Now, participants),
				Description = String.Format("Participants: {0}", participants),
				Duration = 60,
				MaxConnections = 5,
				FolderId = folderId,
				PlayerId = playerId,
				RecordDateTime = DateTime.Now
			};
		}

		/// <summary>
		/// Create object for new presentation with title
		/// </summary>
		/// <param name="folderId"></param>
		/// <param name="playerId"></param>
		/// <param name="title"></param>
		/// <param name="recordTime"></param>
		/// <returns></returns>
		private object NewPresentationWithTitle(string folderId, string playerId, string title, DateTime recordTime)
		{
			return new NewPresentation
			{
				Title = title,
				Duration = 60,
				FolderId = folderId,
				PlayerId = playerId,
				RecordDateTime = recordTime
			};
		}

		/// <summary>
		/// Uploads a media file to a specified Presentation
		/// </summary>
		/// <param name="presentationId">Presentation to load the media to</param>
		/// <param name="filePath">Full file path to media file to upload</param>
		/// <returns>id of upload Job.  Call GetJob with this id to monitor progress.</returns>
		public string UploadMediaFile(String presentationId, String filePath)
		{
			var fileNameOnly = Path.GetFileName(filePath);
			var options = new RestClientOptions("https://mediasite.ucdavis.edu/mediasite/")
			{
				Authenticator = new MediasiteUtil.Models.Auth(_config.Username, _config.Password, _config.ApiKey)
			};
			var uploadClient = new RestClient(options);
			var request = new RestRequest(String.Format("FileServer/Presentation/{0}/{1}", presentationId, Path.GetFileName(filePath)), Method.Put);
			request.AddFile(fileNameOnly, filePath);
			var results = uploadClient.Execute(request);
			ExpectResponse(HttpStatusCode.Created, request, results);

			return PostMediaUpload(presentationId, fileNameOnly);
		}

		/// <summary>
		/// Uploads a media file to a specified Presentation
		/// </summary>
		/// <param name="presentationId">Presentation to load the media to</param>
		/// <param name="filePath">Full file path to media file to upload</param>
		/// <returns>id of upload Job.  Call GetJob with this id to monitor progress.</returns>
		public Response<string> UploadMediaFileWithResponse(String presentationId, String filePath, long fileLength, string sendAsName)
		{
			var fileNameOnly = Path.GetFileName(filePath);
			var options = new RestClientOptions("https://mediasite.ucdavis.edu/mediasite/")
			{
				Authenticator = new Auth(_config.Username, _config.Password, _config.ApiKey)
			};
			var uploadClient = new RestClient(options);
			var request = new RestRequest(String.Format("FileServer/Presentation/{0}/{1}", presentationId, sendAsName), Method.Put);
			request.Timeout = new TimeSpan(0, 10, 0); // 10 minute timeout for upload
			request.AddFile("file", filePath);
			var results = uploadClient.Execute(request);
			var responseObject = GetResponse(HttpStatusCode.Created, request, results);

			if (responseObject == null || !responseObject.success)
			{
				return responseObject;
			}

			responseObject.InnerResponse = PostMediaUploadWithResponse(presentationId, sendAsName);

			return responseObject;
		}

		/// <summary>
		/// Uploads a media file after it has already been prepared.
		/// </summary>
		/// <param name="presentationId"></param>
		/// <param name="fileName"></param>
		/// <returns></returns>
		private String PostMediaUpload(String presentationId, String fileName)
		{
			var request = new RestRequest(String.Format("Presentations('{0}')/CreateMediaUpload", presentationId), Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(new NewMediaUpload { FileName = fileName });
			var results = Client.Execute<StringValue>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			return results.Data.value;
		}

		/// <summary>
		/// Uploads a media file after it has already been prepared.
		/// </summary>
		/// <param name="presentationId"></param>
		/// <param name="fileName"></param>
		/// <returns></returns>
		private Response<string> PostMediaUploadWithResponse(String presentationId, String fileName)
		{
			var request = new RestRequest(String.Format("Presentations('{0}')/CreateMediaUpload", presentationId), Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(new NewMediaUpload { FileName = fileName });
			var results = Client.Execute<StringValue>(request);

			return GetResponse(HttpStatusCode.OK, request, results, r => r.Data.value);
		}

		/// <summary>
		/// Creates a Presentation
		/// </summary>
		/// <param name="name"></param>
		/// <param name="parentFolderId"></param>
		public Folder CreateFolder(String name, String parentFolderId)
		{
			var request = new RestRequest("Folders", Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(new NewFolder
			{
				Name = name,
				ParentFolderId = parentFolderId
			});
			var results = Client.Execute<Folder>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			return results.Data;
		}
		#endregion

		public Job GetJob(String jobId)
		{
			var request = new RestRequest("Jobs('{id}')", Method.Get);
			request.AddParameter("id", jobId, ParameterType.UrlSegment);
			var results = Client.Execute<Job>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			return results.Data;
		}

		/// <summary>
		/// Create a paged REST request for the given resource with optional additional parameters
		/// </summary>
		/// <param name="resource">Resource to query</param>
		/// <param name="filter">filter</param>
		/// <param name="orderBy">property to order by</param>
		/// <param name="batchSize">size of batch to return (OData $top)</param>
		/// <param name="skip">how many rows to skip (OData $skip)</param>
		/// <returns></returns>
		private RestRequest CreatePagedRestRequest(string resource, string filter, string orderBy, int batchSize, int skip)
		{
			var request = new RestRequest(resource, Method.Get);

			if (!String.IsNullOrEmpty(filter))
			{
				request.AddParameter("$filter", filter);
			}
			if (!string.IsNullOrEmpty(orderBy))
			{
				request.AddParameter("$orderby", orderBy);
			}
			request.AddParameter("$top", batchSize);
			if (skip > 0)
			{
				request.AddParameter("$skip", skip);
			}

			return request;
		}

		public AuthorizationTicket GetAuthTicket(string presentationId, int minutesToLive, string userName)
		{
			var request = new RestRequest("AuthorizationTickets", Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(new NewAuthorizationTicket
			{
				Username = userName,
				//ClientIpAddress = Request.UserHostAddress,
				ResourceId = presentationId,
				MinutesToLive = minutesToLive
			});
			var ticket = Client.Execute<AuthorizationTicket>(request);

			// check for errors
			ExpectResponse(HttpStatusCode.OK, request, ticket);

			return ticket.Data;
		}

		/// <summary>
		/// Return a single folder matching a generic search filter
		/// </summary>
		/// <param name="filter"></param>
		/// <returns></returns>
		private Folder GetFolderWithFilter(string filter)
		{
			// request the folder
			var folders = new List<Folder>();
			var request = CreatePagedRestRequest("Folders", filter, "Name", _batchSize, 0);
			var results = Client.Execute<OData<List<Folder>>>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			folders.AddRange(results.Data.Value);
			
			// check for expected number of results
			ExpectSingleResult(request, folders);

			return folders[0];
		}

		#region Recorders
		/// <summary>
		/// Get a list of all Recorders
		/// </summary>
		/// <returns></returns>
		public List<Recorder> GetRecorders()
		{
			return GetRecorders(null);
		}

		/// <summary>
		/// Get a list of all Recorders whose name starts with the given string
		/// </summary>
		/// <returns>List of all Recorders</returns>
		public List<Recorder> GetRecorders(string startsWith)
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// get all recorders - paged query.  filter afterwards because 'startsWith' function does not like dash characters.
			var recorders = new List<Recorder>();
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest("Recorders", "", "Name", _batchSize, current);
				var results = Client.Execute<OData<List<Recorder>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				recorders.AddRange(results.Data.Value);
			}

			return recorders.Where(r => r.Name.StartsWith(startsWith ?? "")).ToList();
		}
		
		/// <summary>
		/// Finds and returns a recorder by recorder name, exact match
		/// </summary>
		/// <param name="recorderName"></param>
		/// <returns></returns>
		public Recorder FindRecorder(string recorderName)
		{
			// build a filter to find the recorder
			var filter = String.Format("Name eq '{0}'", recorderName);
			return GetRecorderWithFilter(filter);
		}
		
		/// <summary>
		/// Return a single recorder matching a generic search filter
		/// </summary>
		/// <param name="filter"></param>
		/// <returns></returns>
		private Recorder GetRecorderWithFilter(string filter)
		{
			// request the recorder
			var recorders = new List<Recorder>();
			var request = CreatePagedRestRequest("Recorders", filter, "Name", _batchSize, 0);
			var results = Client.Execute<OData<List<Recorder>>>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			recorders.AddRange(results.Data.Value);
			
			// check for expected number of results
			ExpectSingleResult(request, recorders);

			return recorders[0];
		}
		
		/// <summary>
		/// Get the status of an individual Recorder
		/// </summary>
		/// <param name="recorderId">Recorder Id</param>
		/// <returns>Recorder Status</returns>
		public RecorderStatus GetRecorderStatus(string recorderId)
		{
			var request = new RestRequest(String.Format("Recorders('{0}')/Status", recorderId), Method.Get);
			var results = Client.Execute<RecorderStatus>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);

			return results.Data;
		}

		/// <summary>
		/// Gets Recorder Status directly from the Recorder web api for version 7 only of recorder software.
		/// This is a hack to get around the MediaSite server API for Recorder/Status not working properly.
		/// </summary>
		/// <param name="recorderHostName">Host name or IP Address of Recorder</param>
		/// <returns>String representing state of recorder.</returns>
		/// <remarks>See http://mediasite.ucdavis.edu/mediasite/api/v1/$metadata#RecorderStatus for valid states</remarks>
		public string GetRecorderStatusDirectV7(string recorderHostName)
		{
			var client = new RestClient(String.Format("http://{0}:8090/recorderwebapi/v1/", recorderHostName));
			var request = new RestRequest("action/service/RecorderStateJson");
			var results = client.Execute(request);
			ExpectResponse(HttpStatusCode.OK, request, results);

			var capture = Regex.Match(results.Content, "\"RecorderStateString\":\"([^\"]*)\"").Groups;
			return capture.Count > 1 ? capture[1].Value : null;
		}

		/// <summary>
		/// Start a recorder
		/// </summary>
		/// <param name="recorderId">Recorder Id</param>
		public void RecorderStart(string recorderId)
		{
			var request = new RestRequest(String.Format("Recorders('{0}')/Start", recorderId), Method.Post);
			var results = Client.Execute(request);
			ExpectResponse(HttpStatusCode.NoContent, request, results);
		}

		/// <summary>
		/// Stop a recorder
		/// </summary>
		/// <param name="recorderId"></param>
		public void RecorderStop(string recorderId)
		{
			var request = new RestRequest(String.Format("Recorders('{0}')/Stop", recorderId), Method.Post);
			var results = Client.Execute(request);
			ExpectResponse(HttpStatusCode.NoContent, request, results);
		}

		/// <summary>
		/// Login to Recorder API to unlock resources such as image feeds
		/// </summary>
		/// <param name="recorderHostName"></param>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <returns>non-null string representing Ticket if login succeeds</returns>
		public string RecorderLogin(string recorderHostName, string username, string password)
		{
			var client = new RestClient(String.Format("http://{0}:8090/recorderwebapi/v1/", recorderHostName));
			var request = new RestRequest("action/service/Login", Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddHeader("Content-Type", "application/json");
			var json = new NewRecorderLogin { Username = username, Password = password };
			request.AddBody(json);
			var results = client.Execute(request);
			ExpectResponse(HttpStatusCode.OK, request, results);

			var capture = Regex.Match(results.Content, "\"Ticket\":\"([^\"]*)\"").Groups;
			return capture.Count > 1 ? capture[1].Value : null;
		}
		#endregion

		#region Schedules
		/// <summary>
		/// Get a list of all Schedules
		/// </summary>
		/// <returns></returns>
		public List<Schedule> GetSchedules()
		{
			return GetSchedules(null);
		}

		/// <summary>
		/// Get a list of all Schedules on the given Recorder
		/// </summary>
		/// <returns>List of all Recorders</returns>
		public List<Schedule> GetSchedules(string recorderId)
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// filter
			string filter = null;
			if (!String.IsNullOrEmpty(recorderId))
			{
				filter = String.Format("RecorderId eq '{0}'", recorderId);
			}

			// get all schedules - paged query
			var schedules = new List<Schedule>();
			if (returned == _batchSize)	// currently limited to 1 batch
			{
				var request = CreatePagedRestRequest("Schedules", filter, null, _batchSize, current);
				var results = Client.Execute<OData<List<Schedule>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				schedules.AddRange(results.Data.Value);
			}

			return schedules;
		}

		/// <summary>
		/// Get a list of all Templates
		/// </summary>
		/// <returns></returns>
		public List<Template> GetTemplates()
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// get all templates - paged query
			var templates = new List<Template>();
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest("Templates", null, null, _batchSize, current);
				var results = Client.Execute<OData<List<Template>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				templates.AddRange(results.Data.Value);
			}

			return templates;
		}
		
		/// <summary>
		/// Finds and returns a schedule by schedule name, exact match
		/// </summary>
		/// <param name="scheduleName"></param>
		/// <returns></returns>
		public Schedule FindSchedule(string scheduleName)
		{
			// build a filter to find the schedule
			var filter = String.Format("Name eq '{0}'", scheduleName);
			return GetScheduleWithFilter(filter);
		}

        /// <summary>
        /// Finds and returns a schedule by schedule name in a given folder, exact match
        /// </summary>
        /// <param name="scheduleName"></param>
        /// <returns></returns>
        public Schedule FindSchedule(string scheduleName, string parentFolderId)
        {
            // build a filter to find the schedule
			var filter = String.Format("Name eq '{0}' and ParentFolderId eq '{1}'", scheduleName, parentFolderId);
            return GetScheduleWithFilter(filter);
        }

        /// <summary>
        /// Return a single schedule matching a generic search filter
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        private Schedule GetScheduleWithFilter(string filter)
		{
			// request the schedule
			var schedules = new List<Schedule>();
			var request = CreatePagedRestRequest("Schedules", filter, "Name", _batchSize, 0);
			var results = Client.Execute<OData<List<Schedule>>>(request);
			ExpectResponse(HttpStatusCode.OK, request, results);
			schedules.AddRange(results.Data.Value);
			
			// check for expected number of results
			ExpectSingleResult(request, schedules);

			return schedules[0];
		}
		
		/// <summary>
		/// Finds and returns a schedule's recurrences by schedule ID, exact match
		/// </summary>
		/// <param name="scheduleId"></param>
		/// <returns></returns>
		public List<Recurrences> GetScheduleRecurrences(string scheduleId)
		{
			// pagination
			var returned = _batchSize;
			var current = 0;

			// get all recurrences - paged query
			var recurrences = new List<Recurrences>();
			var resource = String.Format("Schedules('{0}')/Recurrences", scheduleId);
			while (returned == _batchSize)
			{
				var request = CreatePagedRestRequest(resource, null, "Name", _batchSize, 0);
				var results = Client.Execute<OData<List<Recurrences>>>(request);
				ExpectResponse(HttpStatusCode.OK, request, results);
				current += returned = results.Data.Value.Count;
				recurrences.AddRange(results.Data.Value);
			}

			return recurrences;
		}
		
		/// <summary>
		/// Creates a Schedule
		/// </summary>
		/// <param name="name"></param>
		/// <param name="folderId"></param>
		/// <param name="templateId"></param>
		/// <param name="recorderId"></param>
		public Schedule CreateSchedule(String name, String folderId, String templateId, String recorderId)
		{
			// Create schedule
			var request = new RestRequest("Schedules", Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(new NewSchedule()
			{
				Name = name,
				TitleType = "ScheduleNameAndAirDateTime",
				FolderId = folderId,
				ScheduleTemplateId = templateId,
				IsUploadAutomatic = true,
				RecorderId = recorderId,
				AdvanceCreationTime = 0,
				AdvanceLoadTimeInSeconds = 0,
				CreatePresentation = true,
				LoadPresentation = true,
				AutoStart = true,
				AutoStop = true,
				DeleteInactive = false
			});
			var response = Client.Execute<Schedule>(request);
			ExpectResponse(HttpStatusCode.OK, request, response);
			
			return response.Data;
		}

		/// <summary>
		/// Adds a weekly recurrence to a schedule in UTC
		/// </summary>
		/// <param name="scheduleId"></param>
		/// <param name="recordDurationMs"></param>
		/// <param name="startRecurrenceDateTime"></param>
		/// <param name="endRecurrenceDate"></param>
		/// <param name="daysOfTheWeek"></param>
		/// <returns></returns>
		public Recurrences AddWeeklyRecurrence(string scheduleId, int recordDurationMs, DateTime startRecurrenceDateTime,
			DateTime endRecurrenceDate, string daysOfTheWeek)
		{
			// Add recurrences to schedule
			var resource = String.Format("Schedules('{0}')/Recurrences", scheduleId);
			var request = new RestRequest(resource, Method.Post);
			request.RequestFormat = DataFormat.Json;
			request.AddJsonBody(new NewWeeklyRecurrences()
			{
				RecordDuration = recordDurationMs,
				StartRecordDateTime = startRecurrenceDateTime,
				EndRecordDateTime = endRecurrenceDate,
				RecurrencePattern = "Weekly",
				RecurrenceFrequency = 1,
				WeekDayOnly = true,
				DaysOfTheWeek = daysOfTheWeek
			});
			var response = Client.Execute<Recurrences>(request);
			ExpectResponse(HttpStatusCode.OK, request, response);

			return response.Data;
		}
		#endregion

		private static void ExpectResponse(HttpStatusCode expectedStatusCode, RestRequest request, RestResponse response)
		{

			if (response.StatusCode != expectedStatusCode || response.ErrorMessage != null)
			{
				throw new Exception(String.Format("{0} returned http status = '{1}', error = '{2}', content = '{3}'",
					request.Resource, response.StatusDescription, response.ErrorMessage, response.Content));
			}
		}

		private static void ExpectSingleResult(RestRequest request, IList list)
		{
			if (list.Count != 1)
			{
				throw new Exception(String.Format("{0} found {1} items", request.Resource, list.Count));
			}
		}

		/// <summary>
		/// Returns response wrapped in response metadata including success indicator based on expected http response status code
		/// </summary>
		/// <param name="expectedStatusCode"></param>
		/// <param name="request"></param>
		/// <param name="response"></param>
		/// <returns></returns>
		private static Response<String> GetResponse(HttpStatusCode expectedStatusCode, RestRequest request, RestResponse response)
		{
			return new Response<String>
			{
				success = response.StatusCode == expectedStatusCode && response.ErrorMessage == null,
				resource = request.Resource,
				statusCode = response.StatusCode,
				statusDescription = response.StatusDescription,
				errorMessage = response.ErrorMessage,
				content = response.Content
			};
		}

		/// <summary>
		/// Returns response wrapped in response metadata including success indicator based on expected http response status code
		/// </summary>
		/// <param name="expectedStatusCode"></param>
		/// <param name="request"></param>
		/// <param name="response"></param>
		/// <returns></returns>
		private static Response<String> GetResponse(HttpStatusCode expectedStatusCode, RestRequest request, RestResponse<StringValue> response, Func<RestResponse<StringValue>, String> contentSelector)
		{
			return new Response<String>
			{
				success = response.StatusCode == expectedStatusCode && response.ErrorMessage == null,
				resource = request.Resource,
				statusCode = response.StatusCode,
				statusDescription = response.StatusDescription,
				errorMessage = response.ErrorMessage,
				content = contentSelector(response)
			};
		}
	}
}