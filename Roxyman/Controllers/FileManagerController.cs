using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
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
            _systemRootPath = HttpContext.Current.Server.MapPath("~/");
            _tempPath = _systemRootPath + "\\temp";
            _filesRootPath = "/Uploads";
            _filesRootVirtual = "/Uploads";
            // Load Fileman settings
            LoadSettings();
        }
        private void LoadSettings()
        {
            _settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(_systemRootPath + "/conf.json"));
            string langFile = _systemRootPath + "/lang/" + GetSetting("LANG") + ".json";
            if (!System.IO.File.Exists(langFile)) langFile = _systemRootPath + "/lang/en.json";
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
        private void CheckPath(string path)
        {
            if (FixPath(path).IndexOf(GetFilesRoot()) != 0) throw new Exception("Access to " + path + " is denied");
        }
        private double LinuxTimestamp(DateTime d)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0).ToLocalTime();
            TimeSpan timeSpan = (d.ToLocalTime() - epoch);
            return timeSpan.TotalSeconds;
        }
        private string MakeUniqueFilename(string dir, string filename)
        {
            string ret = filename;
            int i = 0;
            while (System.IO.File.Exists(Path.Combine(dir, ret)))
            {
                i++;
                ret = Path.GetFileNameWithoutExtension(filename) + "-Copy" + i.ToString() + Path.GetExtension(filename);
            }
            return ret;
        }
        private void CopyDir(string path, string dest)
        {
            if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
            foreach (string f in Directory.GetFiles(path))
            {
                FileInfo file = new FileInfo(f);
                if (!System.IO.File.Exists(Path.Combine(dest, file.Name))) System.IO.File.Copy(f, Path.Combine(dest, file.Name));
            }
            foreach (string d in Directory.GetDirectories(path)) CopyDir(d, Path.Combine(dest, new DirectoryInfo(d).Name));
        }

        private bool IsAjaxUpload()
        {
            return (!string.IsNullOrEmpty(HttpContext.Current.Request.QueryString["method"]) && HttpContext.Current.Request.QueryString["method"].ToString() == "ajax");
        }
        private bool CanHandleFile(string filename)
        {
            bool ret = false;
            FileInfo file = new FileInfo(filename);
            string ext = file.Extension.Replace(".", "").ToLower();
            string setting = GetSetting("FORBIDDEN_UPLOADS").Trim().ToLower();
            if (setting != "")
            {
                ArrayList tmp = new ArrayList();
                tmp.AddRange(Regex.Split(setting, "\\s+"));
                if (!tmp.Contains(ext)) ret = true;
            }
            setting = GetSetting("ALLOWED_UPLOADS").Trim().ToLower();
            if (setting != "")
            {
                ArrayList tmp = new ArrayList();
                tmp.AddRange(Regex.Split(setting, "\\s+"));
                if (!tmp.Contains(ext)) ret = false;
            }
            return ret;
        }

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
        public IHttpActionResult FILESLIST(string d, string type)
        {
            try
            {
                d = MakePhysicalPath(d);
                CheckPath(d);
                string fullPath = FixPath(d);
                List<string> files = GetFiles(fullPath, type);
                string result = "";
                for (int i = 0; i < files.Count; i++)
                {
                    FileInfo f = new FileInfo(files[i]);
                    int w = 0, h = 0;
                    result += (result != "" ? "," : "") +
                        "{" +
                        "\"p\":\"" + MakeVirtualPath(d) + "/" + f.Name + "\"" +
                        ",\"t\":\"" + Math.Ceiling(LinuxTimestamp(f.LastWriteTime)).ToString() + "\"" +
                        ",\"s\":\"" + f.Length.ToString() + "\"" +
                        ",\"w\":\"" + w.ToString() + "\"" +
                        ",\"h\":\"" + h.ToString() + "\"" +
                        "}";
                }
                return Content(HttpStatusCode.OK, "[" + result + "]", new JsonMediaTypeFormatter(), new MediaTypeHeaderValue("application/json"));
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult COPYDIR(string d, string n)
        {
            try
            {
                d = MakePhysicalPath(d);
                n = MakePhysicalPath(n);
                CheckPath(d);
                CheckPath(n);
                DirectoryInfo dir = new DirectoryInfo(FixPath(d));
                DirectoryInfo newDir = new DirectoryInfo(FixPath(n + "/" + dir.Name));
                if (!dir.Exists) throw new Exception(LangRes("E_CopyDirInvalidPath"));
                else if (newDir.Exists) throw new Exception(LangRes("E_DirAlreadyExists"));
                else CopyDir(dir.FullName, newDir.FullName);
                return Content(HttpStatusCode.OK, GetSuccessRes(), new JsonMediaTypeFormatter(), new MediaTypeHeaderValue("application/json"));
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult CREATEDIR(string d, string n)
        {
            try
            {
                d = MakePhysicalPath(d);
                CheckPath(d);
                d = FixPath(d);
                if (!Directory.Exists(d)) throw new Exception(LangRes("E_CreateDirInvalidPath"));
                else
                {
                    try
                    {
                        d = Path.Combine(d, n);
                        if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                        return Content(HttpStatusCode.OK, GetSuccessRes(), new JsonMediaTypeFormatter(), new MediaTypeHeaderValue("application/json"));
                    }
                    catch (Exception) { throw new Exception(LangRes("E_CreateDirFailed")); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult DELETEDIR(string d)
        {
            try
            {
                d = MakePhysicalPath(d);
                CheckPath(d);
                d = FixPath(d);
                if (!Directory.Exists(d)) throw new Exception(LangRes("E_DeleteDirInvalidPath"));
                else if (d == GetFilesRoot()) throw new Exception(LangRes("E_CannotDeleteRoot"));
                else if (Directory.GetDirectories(d).Length > 0 || Directory.GetFiles(d).Length > 0) throw new Exception(LangRes("E_DeleteNonEmpty"));
                else
                {
                    try
                    {
                        Directory.Delete(d);
                        return Content(HttpStatusCode.OK, GetSuccessRes());
                    }
                    catch (Exception) { throw new Exception(LangRes("E_CannotDeleteDir")); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult DELETEFILE(string f)
        {
            try
            {
                f = MakePhysicalPath(f);
                CheckPath(f);
                f = FixPath(f);
                if (!System.IO.File.Exists(f)) throw new Exception(LangRes("E_DeleteFileInvalidPath"));
                else
                {
                    try
                    {
                        System.IO.File.Delete(f);
                        return Content(HttpStatusCode.OK, GetSuccessRes());
                    }
                    catch (Exception) { throw new Exception(LangRes("E_DeletеFile")); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }
        public HttpResponseMessage DOWNLOAD(string f)
        {
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK);
            try
            {
                f = MakePhysicalPath(f);
                CheckPath(f);
                FileInfo file = new FileInfo(FixPath(f));
                var stream = new FileStream(FixPath(f), FileMode.Open, FileAccess.Read);
                result.Content = new StreamContent(stream);
                result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = file.FullName
                };
                result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return result;
            }
            catch (Exception ex)
            {
                result.StatusCode = HttpStatusCode.BadRequest;
                return result;
            }
        }
        public IHttpActionResult DOWNLOADDIR(string d)
        {
            try
            {
                d = MakePhysicalPath(d);
                d = FixPath(d);
                if (!Directory.Exists(d)) throw new Exception(LangRes("E_CreateArchive"));
                string dirName = new FileInfo(d).Name;
                string tmpZip = _tempPath + "/" + dirName + ".zip";
                if (System.IO.File.Exists(tmpZip)) System.IO.File.Delete(tmpZip);
                ZipFile.CreateFromDirectory(d, tmpZip, CompressionLevel.Fastest, true);

                IHttpActionResult response;
                HttpResponseMessage responseMsg = new HttpResponseMessage(HttpStatusCode.OK);
                var file = File.ReadAllBytes(tmpZip);
                responseMsg.Content = new ByteArrayContent(file);
                responseMsg.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
                responseMsg.Content.Headers.ContentDisposition.FileName = dirName;
                responseMsg.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                response = ResponseMessage(responseMsg);
                return response;
            }
            catch (Exception ex)
            {
                return BadRequest(GetErrorRes(ex.Message));
            }
        }

        public IHttpActionResult MOVEDIR(string d, string n)
        {
            try
            {
                d = MakePhysicalPath(d);
                n = MakePhysicalPath(n);
                CheckPath(d);
                CheckPath(n);
                DirectoryInfo source = new DirectoryInfo(FixPath(d));
                DirectoryInfo dest = new DirectoryInfo(FixPath(Path.Combine(n, source.Name)));
                if (dest.FullName.IndexOf(source.FullName) == 0) throw new Exception(LangRes("E_CannotMoveDirToChild"));
                else if (!source.Exists) throw new Exception(LangRes("E_MoveDirInvalisPath"));
                else if (dest.Exists) throw new Exception(LangRes("E_DirAlreadyExists"));
                else
                {
                    try
                    {
                        source.MoveTo(dest.FullName);
                        return Content(HttpStatusCode.OK, GetSuccessRes());
                    }
                    catch (Exception) { throw new Exception(LangRes("E_MoveDir") + " \"" + d + "\""); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult MOVEFILE(string f, string n)
        {
            try
            {
                f = MakePhysicalPath(f);
                n = MakePhysicalPath(n);
                CheckPath(f);
                CheckPath(n);
                FileInfo source = new FileInfo(FixPath(f));
                FileInfo dest = new FileInfo(FixPath(n));
                if (!source.Exists) throw new Exception(LangRes("E_MoveFileInvalisPath"));
                else if (dest.Exists) throw new Exception(LangRes("E_MoveFileAlreadyExists"));
                else if (!CanHandleFile(dest.Name)) throw new Exception(LangRes("E_FileExtensionForbidden"));
                else
                {
                    try
                    {
                        source.MoveTo(dest.FullName);
                        return Content(HttpStatusCode.OK, GetSuccessRes());
                    }
                    catch (Exception) { throw new Exception(LangRes("E_MoveFile") + " \"" + f + "\""); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult RENAMEDIR(string d, string n)
        {
            try
            {
                d = MakePhysicalPath(d);
                CheckPath(d);
                DirectoryInfo source = new DirectoryInfo(FixPath(d));
                DirectoryInfo dest = new DirectoryInfo(Path.Combine(source.Parent.FullName, n));
                if (source.FullName == GetFilesRoot()) throw new Exception(LangRes("E_CannotRenameRoot"));
                else if (!source.Exists) throw new Exception(LangRes("E_RenameDirInvalidPath"));
                else if (dest.Exists) throw new Exception(LangRes("E_DirAlreadyExists"));
                else
                {
                    try
                    {
                        source.MoveTo(dest.FullName);
                        return Content(HttpStatusCode.OK, GetSuccessRes());
                    }
                    catch (Exception) { throw new Exception(LangRes("E_RenameDir") + " \"" + d + "\""); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        public IHttpActionResult RENAMEFILE(string f, string n)
        {
            try
            {
                f = MakePhysicalPath(f);
                CheckPath(f);
                FileInfo source = new FileInfo(FixPath(f));
                FileInfo dest = new FileInfo(Path.Combine(source.Directory.FullName, n));
                if (!source.Exists) throw new Exception(LangRes("E_RenameFileInvalidPath"));
                else if (!CanHandleFile(n)) throw new Exception(LangRes("E_FileExtensionForbidden"));
                else
                {
                    try
                    {
                        source.MoveTo(dest.FullName);
                        return Content(HttpStatusCode.OK, GetSuccessRes());
                    }
                    catch (Exception ex) { throw new Exception(ex.Message + "; " + LangRes("E_RenameFile") + " \"" + f + "\""); }
                }
            }
            catch (Exception ex) { return Content(HttpStatusCode.BadRequest, GetErrorRes(ex.Message)); }
        }

        [HttpPost]
        public string UPLOAD(FormDataCollection data)
        {
            try
            {
                string d = data["d"];
                d = MakePhysicalPath(d);
                CheckPath(d);
                d = FixPath(d);
                string res = GetSuccessRes();
                bool hasErrors = false;
                try
                {
                    foreach (string fileString in HttpContext.Current.Request.Files)
                    {
                        var file = HttpContext.Current.Request.Files[fileString];
                        if (CanHandleFile(file.FileName))
                        {
                            FileInfo f = new FileInfo(file.FileName);
                            string filename = MakeUniqueFilename(d, f.Name);
                            string dest = Path.Combine(d, filename);
                            file.SaveAs(dest);
                        }
                        else
                        {
                            hasErrors = true;
                            res = GetSuccessRes(LangRes("E_UploadNotAll"));
                        }
                    }
                }
                catch (Exception ex) { res = GetErrorRes(ex.Message); }
                if (IsAjaxUpload())
                {
                    if (hasErrors) res = GetErrorRes(LangRes("E_UploadNotAll"));
                    return res;
                }
                else return "<script>parent.fileUploaded(" + res + ");</script>";
            }
            catch (Exception ex)
            {
                if (!IsAjaxUpload()) return "<script>parent.fileUploaded(" + GetErrorRes(LangRes("E_UploadNoFiles")) + ");</script>";
                else return GetErrorRes(ex.Message);
            }
        }

    }
}