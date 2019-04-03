using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Http;

namespace Roxyman.Controllers
{
    public class FileManagerController : ApiController
    {
        private string _systemRootPath;
        private string _tempPath;
        private string _filesRootPath;
        private string _filesRootVirtual;
        private Dictionary<string, string> _settings;
        private Dictionary<string, string> _lang = null;
        public FileManagerController()
        {
            _systemRootPath = HttpContext.Current.Server.MapPath("");
            _tempPath = _systemRootPath + "\\wwwroot\\CMS\\Temp";
            _filesRootPath = "/wwwroot/CMS/Content";
            _filesRootVirtual = "/CMS/Content";
            // Load Fileman settings
            LoadSettings();
        }
        private void LoadSettings()
        {
            _settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(_systemRootPath + "/wwwroot/lib/fileman/conf.json"));
            string langFile = _systemRootPath + "/wwwroot/lib/fileman/lang/" + GetSetting("LANG") + ".json";
            if (!System.IO.File.Exists(langFile)) langFile = _systemRootPath + "/wwwroot/lib/fileman/lang/en.json";
            _lang = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(langFile));
        }

        private string GetSetting(string name)
        {
            string ret = "";
            if (_settings.ContainsKey(name)) ret = _settings[name];
            return ret;
        }

        private ArrayList ListDirs(string path)
        {
            string[] dirs = Directory.GetDirectories(path);
            ArrayList ret = new ArrayList();
            foreach (string dir in dirs)
            {
                ret.Add(dir);
                ret.AddRange(ListDirs(dir));
            }
            return ret;
        }

        private string GetFilesRoot()
        {
            string ret = _filesRootPath;
            //if (GetSetting("SESSION_PATH_KEY") != "" && HttpContext.Current.Session.GetString(GetSetting("SESSION_PATH_KEY")) != null) ret = HttpContext.Session.GetString(GetSetting("SESSION_PATH_KEY"));
            ret = FixPath(ret);
            return ret;
        }
        private string FixPath(string path)
        {
            path = path.TrimStart('~');
            if (!path.StartsWith("/")) path = "/" + path;
            return _systemRootPath + path;
        }
        private string MakeVirtualPath(string path)
        {
            return !path.StartsWith(_filesRootPath) ? path : _filesRootVirtual + path.Substring(_filesRootPath.Length);
        }

        private string MakePhysicalPath(string path)
        {
            return !path.StartsWith(_filesRootVirtual) ? path : _filesRootPath + path.Substring(_filesRootVirtual.Length);
        }


        private List<string> GetFiles(string path, string type)
        {
            List<string> ret = new List<string>();
            if (type == "#" || type == null) type = "";
            string[] files = Directory.GetFiles(path);
            foreach (string f in files) { if ((GetFileType(new FileInfo(f).Extension) == type) || (type == "")) ret.Add(f); }
            return ret;
        }
        private string GetFileType(string ext)
        {
            string ret = "file";
            ext = ext.ToLower();
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif") ret = "image";
            else if (ext == ".swf" || ext == ".flv") ret = "flash";
            return ret;
        }
        private string GetResultStr(string type, string msg)
        {
            return "{\"res\":\"" + type + "\",\"msg\":\"" + msg.Replace("\"", "\\\"") + "\"}";
        }

        private string LangRes(string name) { return _lang.ContainsKey(name) ? _lang[name] : name; }

        private string GetSuccessRes(string msg) { return GetResultStr("ok", msg); }

        private string GetSuccessRes() { return GetSuccessRes(""); }
        private string GetErrorRes(string msg) { return GetResultStr("error", msg); }

        [AllowAnonymous, ActionName("")]
        public string Get() { return "Filemanager - access to API requires Authorisation"; }


        public IHttpActionResult DIRLIST(string type)
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(GetFilesRoot());
                if (!d.Exists) throw new Exception("Invalid files root directory. Check your configuration.");
                ArrayList dirs = ListDirs(d.FullName);
                dirs.Insert(0, d.FullName);
                string localPath = _systemRootPath;
                string result = "";
                for (int i = 0; i < dirs.Count; i++)
                {
                    string dir = (string)dirs[i];
                    result += (result != "" ? "," : "") + "{\"p\":\"" + MakeVirtualPath(dir.Replace(localPath, "").Replace("\\", "/")) + "\",\"f\":\"" + GetFiles(dir, type).Count.ToString() + "\",\"d\":\"" + Directory.GetDirectories(dir).Length.ToString() + "\"}";
                }
                return Content(HttpStatusCode.OK, "[" + result + "]", new JsonMediaTypeFormatter(), new MediaTypeHeaderValue("application/json"));
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }



    }
}