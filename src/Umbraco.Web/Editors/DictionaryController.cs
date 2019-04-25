using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using CsvHelper;
using Umbraco.Core;
using Umbraco.Core.Cache;
using Umbraco.Core.Configuration;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence;
using Umbraco.Core.Services;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Web.Mvc;
using Umbraco.Web.WebApi;
using Umbraco.Web.WebApi.Filters;
using Constants = Umbraco.Core.Constants;
using Notification = Umbraco.Web.Models.ContentEditing.Notification;

namespace Umbraco.Web.Editors
{
    /// <inheritdoc />
    /// <summary>
    /// The API controller used for editing dictionary items
    /// </summary>
    /// <remarks>
    /// The security for this controller is defined to allow full CRUD access to dictionary if the user has access to either:
    /// Dictionary
    /// </remarks>
    [PluginController("UmbracoApi")]
    [UmbracoTreeAuthorize(Constants.Trees.Dictionary)]
    [EnableOverrideAuthorization]
    public class DictionaryController : BackOfficeNotificationsController
    {
        public DictionaryController(IGlobalSettings globalSettings, IUmbracoContextAccessor umbracoContextAccessor, ISqlContext sqlContext, ServiceContext services, AppCaches appCaches, IProfilingLogger logger, IRuntimeState runtimeState, UmbracoHelper umbracoHelper)
            : base(globalSettings, umbracoContextAccessor, sqlContext, services, appCaches, logger, runtimeState, umbracoHelper)
        {
        }

        /// <summary>
        /// Deletes a data type with a given ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns><see cref="HttpResponseMessage"/></returns>
        [HttpDelete]
        [HttpPost]
        public HttpResponseMessage DeleteById(int id)
        {
            var foundDictionary = Services.LocalizationService.GetDictionaryItemById(id);

            if (foundDictionary == null)
                throw new HttpResponseException(HttpStatusCode.NotFound);

            Services.LocalizationService.Delete(foundDictionary, Security.CurrentUser.Id);

            return Request.CreateResponse(HttpStatusCode.OK);
        }

        /// <summary>
        /// Creates a new dictionary item
        /// </summary>
        /// <param name="parentId">
        /// The parent id.
        /// </param>
        /// <param name="key">
        /// The key.
        /// </param>
        /// <returns>
        /// The <see cref="HttpResponseMessage"/>.
        /// </returns>
        [HttpPost]
        public HttpResponseMessage Create(int parentId, string key)
        {
            if (string.IsNullOrEmpty(key))
                return Request
                    .CreateNotificationValidationErrorResponse("Key can not be empty."); // TODO: translate

            if (Services.LocalizationService.DictionaryItemExists(key))
            {
                var message = Services.TextService.Localize(
                     "dictionaryItem/changeKeyError",
                     Security.CurrentUser.GetUserCulture(Services.TextService, GlobalSettings),
                     new Dictionary<string, string> { { "0", key } });
                return Request.CreateNotificationValidationErrorResponse(message);
            }

            try
            {
                Guid? parentGuid = null;

                if (parentId > 0)
                    parentGuid = Services.LocalizationService.GetDictionaryItemById(parentId).Key;

                var item = Services.LocalizationService.CreateDictionaryItemWithIdentity(
                    key,
                    parentGuid,
                    string.Empty);


                return Request.CreateResponse(HttpStatusCode.OK, item.Id);
            }
            catch (Exception ex)
            {
                Logger.Error(GetType(), ex, "Error creating dictionary with {Name} under {ParentId}", key, parentId);
                return Request.CreateNotificationValidationErrorResponse("Error creating dictionary item");
            }
        }

        /// <summary>
        /// Gets a dictionary item by id
        /// </summary>
        /// <param name="id">
        /// The id.
        /// </param>
        /// <returns>
        /// The <see cref="DictionaryDisplay"/>.
        /// </returns>
        /// <exception cref="HttpResponseException">
        ///  Returns a not found response when dictionary item does not exist
        /// </exception>
        public DictionaryDisplay GetById(int id)
        {
            var dictionary = Services.LocalizationService.GetDictionaryItemById(id);

            if (dictionary == null)
                throw new HttpResponseException(HttpStatusCode.NotFound);

            return Mapper.Map<IDictionaryItem, DictionaryDisplay>(dictionary);
        }

        /// <summary>
        /// Exports translations for the languages passed as the parameter
        /// </summary>
        /// <param name="languageIds"></param>
        /// <returns></returns>
        [HttpGet]
        public HttpResponseMessage ExportDictionaryItems([FromUri]int[] languageIds)
        {
            var memory = new MemoryStream();
            try
            {
                var writer = new StreamWriter(memory);
                var csvWriter = new CsvWriter(writer, NewCsvConfiguration());

                //Writer headers
                csvWriter.WriteField("");
                //Write the culture name for each selected language as a header (also validates that all languageIds passed are valid)
                var languageIdsFromDatabase = new List<int>();
                foreach (var lang in Services.LocalizationService.GetAllLanguages().Where(x => languageIds.Contains(x.Id)))
                {
                    languageIdsFromDatabase.Add(lang.Id);
                    csvWriter.WriteField(lang.CultureName);
                }
                languageIds = languageIdsFromDatabase.ToArray();

                //Writer dictionary items
                var rootNodes = Services.LocalizationService.GetRootDictionaryItems();
                writeNodes(csvWriter, languageIds, rootNodes);

                //Flush the streams and go back to the begin of the memory stream so we can read it to a string
                csvWriter.Flush();
                writer.Flush();
                memory.Seek(0, SeekOrigin.Begin);
                var reader = new StreamReader(memory);
                var data = reader.ReadToEnd();

                string filename = $"translations-{DateTime.Now.ToString(@"yyyy\-MM\-dd")}.csv";

                var response = new HttpResponseMessage
                {
                    Content = new StringContent(data)
                    {
                        Headers =
                        {
                            ContentDisposition = new ContentDispositionHeaderValue("attachment")
                            {
                                FileName = filename
                            },
                            ContentType = new MediaTypeHeaderValue("application/octet-stream")
                        }
                    }
                };

                // Set custom header so umbRequestHelper.downloadFile can save the correct filename
                response.Headers.Add("x-filename", filename);

                return response;
            }
            finally
            {
                //Finally clear up the memory stream no matter the response type
                memory.Dispose();
            }
        }

        /// <summary>
        /// Writes translations for a collection of IDictionaryItem recursivly (writing the nodes descendants as well)
        /// </summary>
        /// <param name="csvWriter">Stream to write to</param>
        /// <param name="languageIds">Languages for which the translations should be written to the stream</param>
        /// <param name="nodes">IDictionaryItems to be written to the stream</param>
        private void writeNodes(CsvWriter csvWriter, int[] languageIds, IEnumerable<IDictionaryItem> nodes)
        {
            foreach (var node in nodes.OrderBy(ItemSort()))
            {
                csvWriter.NextRecord();
                csvWriter.WriteField(node.ItemKey);
                foreach (var langId in languageIds)
                {
                    csvWriter.WriteField(node.GetTranslatedValue(langId));
                }

                var children = Services.LocalizationService.GetDictionaryItemChildren(node.Key);
                if (children != null && children.Any())
                {
                    writeNodes(csvWriter, languageIds, children);
                }
            }
        }

        private static CsvHelper.Configuration.Configuration NewCsvConfiguration() => new CsvHelper.Configuration.Configuration
        {
            MissingFieldFound = null //Set missing field found to null so CsvReader wont throw exceptions when we try to fetch non existing fields
        };


        [HttpPost]
        [FileUploadCleanupFilter(false)]
        public async Task<IHttpActionResult> ImportDictionaryItems()
        {
            string filepath = null;
            try
            {
                if (Request.Content.IsMimeMultipartContent() == false)
                {
                    return base.ResponseMessage(new HttpResponseMessage(HttpStatusCode.UnsupportedMediaType));
                }

                var root = IOHelper.MapPath(SystemDirectories.TempData.EnsureEndsWith('/') + "FileUploads");
                Directory.CreateDirectory(root);
                var provider = new MultipartFormDataStreamProvider(root);
                var result = await Request.Content.ReadAsMultipartAsync(provider);

                //must have a file
                if (result.FileData.Count == 0)
                {
                    return NotFound();
                }

                var overrideValues = (result.FormData["override"] ?? "0").Equals("1");
                var encoding = (result.FormData["encoding"] ?? "0");
                var delimiter = (result.FormData["delimiter"] ?? "0");
                var confirmed = (result.FormData["confirmed"] ?? "0").Equals("1");

                var file = result.FileData[0];
                var fileName = file.Headers.ContentDisposition.FileName.Trim('\"');
                var ext = fileName.Substring(fileName.LastIndexOf('.') + 1).ToLower();

                // renaming the file because MultipartFormDataStreamProvider has created a random fileName instead of using the name from the
                // content-disposition for more than 6 years now. Creating a CustomMultipartDataStreamProvider deriving from MultipartFormDataStreamProvider
                // seems like a cleaner option, but I'm not sure where to put it and renaming only takes one line of code.
                filepath = root + "\\" + fileName;
                System.IO.File.Move(result.FileData[0].LocalFileName, filepath);

                if (!ext.InvariantEquals("csv"))
                {
                    return BadRequest("Unsupported file extension");
                }

                var csvConfiguration = NewCsvConfiguration();
                switch (encoding.ToLower())
                {
                    case "utf-8": csvConfiguration.Encoding = Encoding.UTF8; break;
                    case "utf-32": csvConfiguration.Encoding = Encoding.UTF32; break;
                    case "windows-1252": csvConfiguration.Encoding = Encoding.GetEncoding("Windows-1252"); break;
                    case "iso-8859-1": csvConfiguration.Encoding = Encoding.GetEncoding("ISO-8859-1"); break;
                    case "unicode": csvConfiguration.Encoding = Encoding.Unicode; break;
                    default: csvConfiguration.Encoding = Encoding.ASCII; break;
                }
                csvConfiguration.Delimiter = delimiter; //If the current thread runs on i.e. Danish culture, the default delimiter is semicolon(;). That's why we specify the delimiter

                using (var reader = new StreamReader(filepath, csvConfiguration.Encoding))
                {
                    var csvReader = new CsvReader(reader, csvConfiguration);

                    //Read first line/row of data
                    if (!csvReader.Read())
                    {
                        return base.Ok("");
                    }

                    //Fetch all languages and create a dictionary for fast lookup of culturenames written in the file
                    var allLanguages = Services.LocalizationService.GetAllLanguages().ToDictionary(x => x.CultureName);
                    var langIndexList = new Dictionary<int, ILanguage>();
                    for (int i = 1; i < int.MaxValue; i++)
                    {
                        string fieldValue = csvReader.GetField(i);
                        if (fieldValue == null) //If fieldValue is null we have reach the end of the line
                            break;

                        //Check if language is created in the Umbraco solution
                        //Add it with it index to a dictionary of the languages in the file
                        ILanguage language;
                        if (allLanguages.TryGetValue(fieldValue, out language))
                        {
                            langIndexList.Add(i, language);
                        }
                    }

                    //Read all the non-header rows
                    var changes = new List<IDictionaryItemChange>();
                    var itemsToSave = new List<IDictionaryItem>();
                    while (csvReader.Read())
                    {
                        //ItemKey should always be in the first cell - if we dont find one it might be an empty line
                        var itemKey = csvReader[0];
                        if (itemKey.IsNullOrWhiteSpace())
                            continue;

                        //Throw an exception if non existing ItemKey was read from the file
                        var dictionaryItem = Services.LocalizationService.GetDictionaryItemByKey(itemKey);
                        if (dictionaryItem == null)
                            continue;

                        //Loop throught the languages we found in the header row
                        foreach (var langIndex in langIndexList)
                        {
                            int index = langIndex.Key;
                            var language = langIndex.Value;

                            //Read the localized string from the current row
                            string localizedString = csvReader[index];
                            if (!localizedString.IsNullOrWhiteSpace())
                            {
                                var translation = dictionaryItem.Translations.FirstOrDefault(x => x.LanguageId == language.Id);

                                //if the translation doesnt exist or the value is empty, we write the localized string
                                //if the translation already exist and is not the same as the one from the file, we check if the editor has checked the "Override values"-checkbox
                                if (translation == null || translation.Value.IsNullOrWhiteSpace() || (!translation.Value.Equals(localizedString) && overrideValues))
                                {
                                    changes.Add(new IDictionaryItemChange { ItemKey = itemKey, OldValue = translation.Value, NewValue = localizedString, CultureName = language.CultureName });
                                    Services.LocalizationService.AddOrUpdateDictionaryValue(dictionaryItem, language, localizedString);
                                    
                                    //Add the IDictionaryItem to a list for saving later - This way if theres and error on e.x. line 10, all translations read before line 10 wont be saved
                                    if (!itemsToSave.Contains(dictionaryItem))
                                    {
                                        itemsToSave.Add(dictionaryItem);
                                    }
                                }
                            }
                        }
                    }

                    //Save translations
                    if (confirmed)
                    {
                        Services.LocalizationService.Save(itemsToSave, Security.CurrentUser.Id);
                    }
                    return Ok(changes);
                }
            }
            finally
            {
                //Cleanup after we are done reading or an exception was thrown
                if (!filepath.IsNullOrWhiteSpace())
                    System.IO.File.Delete(filepath);
            }
        }

        public class IDictionaryItemChange
        {
            public string ItemKey { get; set; }
            public string CultureName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
        }

        /// <summary>
        /// Saves a dictionary item
        /// </summary>
        /// <param name="dictionary">
        /// The dictionary.
        /// </param>
        /// <returns>
        /// The <see cref="DictionaryDisplay"/>.
        /// </returns>
        public DictionaryDisplay PostSave(DictionarySave dictionary)
        {
            var dictionaryItem =
                Services.LocalizationService.GetDictionaryItemById(int.Parse(dictionary.Id.ToString()));

            if (dictionaryItem == null)
                throw new HttpResponseException(Request.CreateNotificationValidationErrorResponse("Dictionary item does not exist"));

            var userCulture = Security.CurrentUser.GetUserCulture(Services.TextService, GlobalSettings);

            if (dictionary.NameIsDirty)
            {
                // if the name (key) has changed, we need to check if the new key does not exist
                var dictionaryByKey = Services.LocalizationService.GetDictionaryItemByKey(dictionary.Name);

                if (dictionaryByKey != null && dictionaryItem.Id != dictionaryByKey.Id)
                {

                    var message = Services.TextService.Localize(
                        "dictionaryItem/changeKeyError",
                        userCulture,
                        new Dictionary<string, string> { { "0", dictionary.Name } });
                    ModelState.AddModelError("Name", message);
                    throw new HttpResponseException(Request.CreateValidationErrorResponse(ModelState));
                }

                dictionaryItem.ItemKey = dictionary.Name;
            }

            foreach (var translation in dictionary.Translations)
            {
                Services.LocalizationService.AddOrUpdateDictionaryValue(dictionaryItem,
                    Services.LocalizationService.GetLanguageById(translation.LanguageId), translation.Translation);
            }

            try
            {
                Services.LocalizationService.Save(dictionaryItem);

                var model = Mapper.Map<IDictionaryItem, DictionaryDisplay>(dictionaryItem);

                model.Notifications.Add(new Notification(
                    Services.TextService.Localize("speechBubbles/dictionaryItemSaved", userCulture), string.Empty,
                    NotificationStyle.Success));

                return model;
            }
            catch (Exception ex)
            {
                Logger.Error(GetType(), ex, "Error saving dictionary with {Name} under {ParentId}", dictionary.Name, dictionary.ParentId);
                throw new HttpResponseException(Request.CreateNotificationValidationErrorResponse("Something went wrong saving dictionary"));
            }
        }

        /// <summary>
        /// Retrieves a list with all dictionary items
        /// </summary>
        /// <returns>
        /// The <see cref="IEnumerable{T}"/>.
        /// </returns>
        public IEnumerable<DictionaryOverviewDisplay> GetList()
        {
            var list = new List<DictionaryOverviewDisplay>();

            const int level = 0;

            foreach (var dictionaryItem in Services.LocalizationService.GetRootDictionaryItems().OrderBy(ItemSort()))
            {
                var item = Mapper.Map<IDictionaryItem, DictionaryOverviewDisplay>(dictionaryItem);
                item.Level = 0;
                list.Add(item);

                GetChildItemsForList(dictionaryItem, level + 1, list);
            }

            return list;
        }

        /// <summary>
        /// Get child items for list.
        /// </summary>
        /// <param name="dictionaryItem">
        /// The dictionary item.
        /// </param>
        /// <param name="level">
        /// The level.
        /// </param>
        /// <param name="list">
        /// The list.
        /// </param>
        private void GetChildItemsForList(IDictionaryItem dictionaryItem, int level, ICollection<DictionaryOverviewDisplay> list)
        {
            foreach (var childItem in Services.LocalizationService.GetDictionaryItemChildren(dictionaryItem.Key).OrderBy(ItemSort()))
            {
                var item = Mapper.Map<IDictionaryItem, DictionaryOverviewDisplay>(childItem);
                item.Level = level;
                list.Add(item);

                GetChildItemsForList(childItem, level + 1, list);
            }
        }

        private static Func<IDictionaryItem, string> ItemSort() => item => item.ItemKey;
    }
}
