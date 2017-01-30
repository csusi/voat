﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Voat.Caching;
using Voat.Configuration;
using Voat.Data;
using Voat.Data.Models;
using Voat.Domain.Command;
using Voat.Domain.Query;
using Voat.Models.ViewModels;
using Voat.UI.Utilities;
using Voat.Utilities;

namespace Voat.Controllers
{
    [Authorize]
    public class SubverseModerationController : BaseController
    {
        private readonly voatEntities _db = new voatEntities(true);

        // GET: settings
        [Authorize]
        public ActionResult SubverseSettings(string subversetoshow)
        {
            var subverse = DataCache.Subverse.Retrieve(subversetoshow);

            if (subverse == null)
            {
                ViewBag.SelectedSubverse = "404";
                return SubverseNotFoundErrorView();
            }

            // check that the user requesting to edit subverse settings is subverse owner!
            var subAdmin =
                _db.SubverseModerators.FirstOrDefault(
                    x => x.Subverse == subversetoshow && x.UserName == User.Identity.Name && x.Power <= 2);

            if (subAdmin == null)
                return RedirectToAction("Index", "Home");

            // map existing data to view model for editing and pass it to frontend
            // NOTE: we should look into a mapper which automatically maps these properties to corresponding fields to avoid tedious manual mapping
            var viewModel = new SubverseSettingsViewModel
            {
                Name = subverse.Name,
                Type = subverse.Type,
                SubmissionText = subverse.SubmissionText,
                Description = subverse.Description,
                SideBar = subverse.SideBar,
                Stylesheet = subverse.Stylesheet,
                IsDefaultAllowed = subverse.IsDefaultAllowed,
                SubmitLinkLabel = subverse.SubmitLinkLabel,
                SubmitPostLabel = subverse.SubmitPostLabel,
                IsAdult = subverse.IsAdult,
                IsPrivate = subverse.IsPrivate,
                IsThumbnailEnabled = subverse.IsThumbnailEnabled,
                ExcludeSitewideBans = subverse.ExcludeSitewideBans,
                IsAuthorizedOnly = subverse.IsAuthorizedOnly,
                IsAnonymized = subverse.IsAnonymized,
                MinCCPForDownvote = subverse.MinCCPForDownvote
            };

            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.SubverseName = subverse.Name;
            return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", viewModel);
        }

        // POST: Eddit a Subverse
        [HttpPost]
        [PreventSpam(DelayRequest = 30, ErrorMessage = "Sorry, you are doing that too fast. Please try again in 30 seconds.")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> SubverseSettings(SubverseSettingsViewModel updatedModel)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
                }
                var existingSubverse = _db.Subverses.Find(updatedModel.Name);

                // check if subverse exists before attempting to edit it
                if (existingSubverse != null)
                {
                    // check if user requesting edit is authorized to do so for current subverse
                    if (!ModeratorPermission.HasPermission(User.Identity.Name, updatedModel.Name, Domain.Models.ModeratorAction.ModifySettings))
                    {
                        return new EmptyResult();
                    }
                    //check description for banned domains
                    if (BanningUtility.ContentContainsBannedDomain(existingSubverse.Name, updatedModel.Description))
                    {
                        ModelState.AddModelError(string.Empty, "Sorry, description text contains banned domains.");
                        return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
                    }
                    //check sidebar for banned domains
                    if (BanningUtility.ContentContainsBannedDomain(existingSubverse.Name, updatedModel.SideBar))
                    {
                        ModelState.AddModelError(string.Empty, "Sorry, sidebar text contains banned domains.");
                        return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
                    }

                    // TODO investigate if EntityState is applicable here and use that instead
                    // db.Entry(updatedModel).State = EntityState.Modified;

                    existingSubverse.Description = updatedModel.Description;
                    existingSubverse.SideBar = updatedModel.SideBar;

                    if (updatedModel.Stylesheet != null)
                    {
                        if (updatedModel.Stylesheet.Length < 50001)
                        {
                            existingSubverse.Stylesheet = updatedModel.Stylesheet;
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty, "Sorry, custom CSS limit is set to 50000 characters.");
                            return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
                        }
                    }
                    else
                    {
                        existingSubverse.Stylesheet = updatedModel.Stylesheet;
                    }

                    existingSubverse.IsAdult = updatedModel.IsAdult;
                    existingSubverse.IsPrivate = updatedModel.IsPrivate;
                    existingSubverse.IsThumbnailEnabled = updatedModel.IsThumbnailEnabled;
                    existingSubverse.IsAuthorizedOnly = updatedModel.IsAuthorizedOnly;
                    existingSubverse.ExcludeSitewideBans = updatedModel.ExcludeSitewideBans;
                    existingSubverse.MinCCPForDownvote = updatedModel.MinCCPForDownvote;

                    // these properties are currently not implemented but they can be saved and edited for future use
                    existingSubverse.Type = updatedModel.Type;
                    existingSubverse.SubmitLinkLabel = updatedModel.SubmitLinkLabel;
                    existingSubverse.SubmitPostLabel = updatedModel.SubmitPostLabel;
                    existingSubverse.SubmissionText = updatedModel.SubmissionText;
                    existingSubverse.IsDefaultAllowed = updatedModel.IsDefaultAllowed;

                    if (existingSubverse.IsAnonymized == true && updatedModel.IsAnonymized == false)
                    {
                        ModelState.AddModelError(string.Empty, "Sorry, this subverse is permanently locked to anonymized mode.");
                        return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
                    }

                    // only subverse owners should be able to convert a sub to anonymized mode
                    if (ModeratorPermission.IsLevel(User.Identity.Name, updatedModel.Name, Domain.Models.ModeratorLevel.Owner))
                    {
                        existingSubverse.IsAnonymized = updatedModel.IsAnonymized;
                    }

                    await _db.SaveChangesAsync();

                    //purge new minified CSS
                    CacheHandler.Instance.Remove(CachingKey.SubverseStylesheet(existingSubverse.Name));

                    //purge subvere
                    CacheHandler.Instance.Remove(CachingKey.Subverse(existingSubverse.Name));

                    // go back to this subverse
                    return RedirectToAction("SubverseIndex", "Subverses", new { subverse = updatedModel.Name });

                    // user was not authorized to commit the changes, drop attempt
                }
                ModelState.AddModelError(string.Empty, "Sorry, The subverse you are trying to edit does not exist.");
                return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "Something bad happened.");
                return View("~/Views/Subverses/Admin/SubverseSettings.cshtml", updatedModel);
            }
        }

        // GET: subverse stylesheet editor
        [Authorize]
        public ActionResult SubverseStylesheetEditor(string subversetoshow)
        {
            var subverse = DataCache.Subverse.Retrieve(subversetoshow);

            if (subverse == null)
            {
                ViewBag.SelectedSubverse = "404";
                return SubverseNotFoundErrorView();
            }
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.ModifyCSS))
            {
                return RedirectToAction("Index", "Home");
            }

            // map existing data to view model for editing and pass it to frontend
            var viewModel = new SubverseStylesheetViewModel
            {
                Name = subverse.Name,
                Stylesheet = subverse.Stylesheet
            };

            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.SubverseName = subverse.Name;
            return View("~/Views/Subverses/Admin/SubverseStylesheetEditor.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateInput(false)]
        [PreventSpam(DelayRequest = 30, ErrorMessage = "Sorry, you are doing that too fast. Please try again in 30 seconds.")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> SubverseStylesheetEditor(Subverse updatedModel)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View("~/Views/Subverses/Admin/SubverseSettings.cshtml");
                }
                var existingSubverse = _db.Subverses.Find(updatedModel.Name);

                // check if subverse exists before attempting to edit it
                if (existingSubverse != null)
                {
                    // check if user requesting edit is authorized to do so for current subverse
                    // check that the user requesting to edit subverse settings is subverse owner!
                    if (!ModeratorPermission.HasPermission(User.Identity.Name, existingSubverse.Name, Domain.Models.ModeratorAction.ModifyCSS))
                    {
                        return new EmptyResult();
                    }

                    if (!String.IsNullOrEmpty(updatedModel.Stylesheet))
                    {
                        if (updatedModel.Stylesheet.Length < 50001)
                        {
                            existingSubverse.Stylesheet = updatedModel.Stylesheet;
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty, "Sorry, custom CSS limit is set to 50000 characters.");
                            return View("~/Views/Subverses/Admin/SubverseStylesheetEditor.cshtml");
                        }
                    }
                    else
                    {
                        existingSubverse.Stylesheet = updatedModel.Stylesheet;
                    }

                    await _db.SaveChangesAsync();

                    //purge new minified CSS
                    CacheHandler.Instance.Remove(CachingKey.SubverseStylesheet(existingSubverse.Name));
                    CacheHandler.Instance.Remove(CachingKey.Subverse(existingSubverse.Name));

                    // go back to this subverse
                    return RedirectToAction("SubverseIndex", "Subverses", new { subverse = updatedModel.Name });
                }

                ModelState.AddModelError(string.Empty, "Sorry, The subverse you are trying to edit does not exist.");
                return View("~/Views/Subverses/Admin/SubverseStylesheetEditor.cshtml");
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "Something bad happened.");
                return View("~/Views/Subverses/Admin/SubverseStylesheetEditor.cshtml");
            }
        }
        // GET: subverse moderators for selected subverse
        [Authorize]
        public ActionResult SubverseModerators(string subversetoshow)
        {
            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);
            if (subverseModel == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.InviteMods))
            {
                return RedirectToAction("Index", "Home");
            }

            var subverseModerators = _db.SubverseModerators
                .Where(n => n.Subverse == subversetoshow)
                .Take(20)
                .OrderBy(s => s.Power)
                .ToList();

            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;

            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/SubverseModerators.cshtml", subverseModerators);
        }

        // GET: subverse moderator invitations for selected subverse
        [Authorize]
        public ActionResult ModeratorInvitations(string subversetoshow)
        {
            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);
            if (subverseModel == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.InviteMods))
            {
                return RedirectToAction("Index", "Home");
            }

            var moderatorInvitations = _db.ModeratorInvitations
                .Where(mi => mi.Subverse == subversetoshow)
                .Take(20)
                .OrderBy(s => s.Power)
                .ToList();

            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;

            return PartialView("~/Views/Subverses/Admin/_ModeratorInvitations.cshtml", moderatorInvitations);
        }

        // GET: banned users for selected subverse
        [Authorize]
        public ActionResult SubverseBans(string subversetoshow, int? page)
        {
            const int pageSize = 25;
            int pageNumber = (page ?? 0);

            if (pageNumber < 0)
            {
                return NotFoundErrorView();
            }

            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);

            if (subverseModel == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.Banning))
            {
                return RedirectToAction("Index", "Home");
            }

            var subverseBans = _db.SubverseBans.Where(n => n.Subverse == subversetoshow).OrderByDescending(s => s.CreationDate);
            var paginatedSubverseBans = new PaginatedList<SubverseBan>(subverseBans, page ?? 0, pageSize);

            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;

            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/SubverseBans.cshtml", paginatedSubverseBans);
        }
        #region Banning
        // GET: show add ban view for selected subverse
        [Authorize]
        public ActionResult AddBan(string subversetoshow)
        {
            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);

            if (subverseModel == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.Banning))
            {
                return RedirectToAction("Index", "Home");
            }

            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;
            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/AddBan.cshtml");
        }

        // POST: add a user ban to given subverse
        [Authorize]
        [HttpPost]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> AddBan([Bind(Include = "Id,Subverse,UserName,Reason")] SubverseBan subverseBan)
        {
            if (!ModelState.IsValid)
            {
                return View(subverseBan);
            }
            //check perms
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subverseBan.Subverse, Domain.Models.ModeratorAction.Banning))
            {
                return RedirectToAction("Index", "Home");
            }

            var cmd = new SubverseBanCommand(subverseBan.UserName, subverseBan.Subverse, subverseBan.Reason, true);
            var result = await cmd.Execute();

            if (result.Success)
            {
                return RedirectToAction("SubverseBans");
            }
            else
            {
                ModelState.AddModelError(string.Empty, result.Message);
                ViewBag.SubverseName = subverseBan.Subverse;
                ViewBag.SelectedSubverse = string.Empty;
                return View("~/Views/Subverses/Admin/AddBan.cshtml",
                new SubverseBanViewModel
                {
                    UserName = subverseBan.UserName,
                    Reason = subverseBan.Reason
                });
            }
        }
        // GET: show remove ban view for selected subverse
        [Authorize]
        public ActionResult RemoveBan(string subversetoshow, int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // check if caller is subverse owner, if not, deny listing
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.Banning))
            {
                return RedirectToAction("Index", "Home");
            }
            var subverseBan = _db.SubverseBans.Find(id);

            if (subverseBan == null)
            {
                return HttpNotFound();
            }

            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.SubverseName = subverseBan.Subverse;
            return View("~/Views/Subverses/Admin/RemoveBan.cshtml", subverseBan);
        }

        // POST: remove a ban from given subverse
        [Authorize]
        [HttpPost, ActionName("RemoveBan")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveBan(int id)
        {
            // get ban name for selected subverse
            var banToBeRemoved = await _db.SubverseBans.FindAsync(id);

            if (banToBeRemoved == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            else
            {
                var cmd = new SubverseBanCommand(banToBeRemoved.UserName, banToBeRemoved.Subverse, null, false);
                var response = await cmd.Execute();
                if (response.Success)
                {
                    return RedirectToAction("SubverseBans");
                }
                else
                {
                    ModelState.AddModelError(String.Empty, response.Message);
                    ViewBag.SelectedSubverse = string.Empty;
                    ViewBag.SubverseName = banToBeRemoved.Subverse;
                    return View("~/Views/Subverses/Admin/RemoveBan.cshtml", banToBeRemoved);
                }
            }
        }


        #endregion

        // GET: show remove moderator invitation view for selected subverse
        [Authorize]
        public ActionResult RecallModeratorInvitation(int? invitationId)
        {
            if (invitationId == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var moderatorInvitation = _db.ModeratorInvitations.Find(invitationId);

            if (moderatorInvitation == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            if (!ModeratorPermission.HasPermission(User.Identity.Name, moderatorInvitation.Subverse, Domain.Models.ModeratorAction.InviteMods))
            {
                return RedirectToAction("SubverseModerators");
            }
            //make sure mods can't remove invites 
            var currentModLevel = ModeratorPermission.Level(User.Identity.Name, moderatorInvitation.Subverse);
            if (moderatorInvitation.Power <= (int)currentModLevel && currentModLevel != Domain.Models.ModeratorLevel.Owner)
            {
                return RedirectToAction("SubverseModerators");
            }

            ViewBag.SubverseName = moderatorInvitation.Subverse;
            return View("~/Views/Subverses/Admin/RecallModeratorInvitation.cshtml", moderatorInvitation);
        }

        // POST: remove a moderator invitation from given subverse
        [Authorize]
        [HttpPost, ActionName("RecallModeratorInvitation")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> RecallModeratorInvitation(int invitationId)
        {
            // get invitation to remove
            var invitationToBeRemoved = await _db.ModeratorInvitations.FindAsync(invitationId);
            if (invitationToBeRemoved == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // check if subverse exists
            var subverse = DataCache.Subverse.Retrieve(invitationToBeRemoved.Subverse);
            if (subverse == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            }

            // check if caller has clearance to remove a moderator invitation
            //if (!UserHelper.IsUserSubverseAdmin(User.Identity.Name, subverse.Name) || invitationToBeRemoved.Recipient == User.Identity.Name) return RedirectToAction("Index", "Home");
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subverse.Name, Domain.Models.ModeratorAction.InviteMods))
            {
                return RedirectToAction("Index", "Home");
            }
            //make sure mods can't remove invites 
            var currentModLevel = ModeratorPermission.Level(User.Identity.Name, subverse.Name);
            if (invitationToBeRemoved.Power <= (int)currentModLevel && currentModLevel != Domain.Models.ModeratorLevel.Owner)
            {
                return RedirectToAction("SubverseModerators");
            }

            // execute invitation removal
            _db.ModeratorInvitations.Remove(invitationToBeRemoved);
            await _db.SaveChangesAsync();
            return RedirectToAction("SubverseModerators");
        }



        // GET: show resign as moderator view for selected subverse
        [Authorize]
        public ActionResult ResignAsModerator(string subversetoresignfrom)
        {
            if (subversetoresignfrom == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var subModerator = _db.SubverseModerators.FirstOrDefault(s => s.Subverse == subversetoresignfrom && s.UserName == User.Identity.Name);

            if (subModerator == null)
            {
                return RedirectToAction("Index", "Home");
            }

            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.SubverseName = subModerator.Subverse;

            return View("~/Views/Subverses/Admin/ResignAsModerator.cshtml", subModerator);
        }

        // POST: resign as moderator from given subverse
        [Authorize]
        [HttpPost]
        [ActionName("ResignAsModerator")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> ResignAsModeratorPost(string subversetoresignfrom)
        {
            // get moderator name for selected subverse
            var subModerator = _db.SubverseModerators.FirstOrDefault(s => s.Subverse == subversetoresignfrom && s.UserName == User.Identity.Name);

            if (subModerator == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var subverse = DataCache.Subverse.Retrieve(subModerator.Subverse);
            if (subverse == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // execute removal
            _db.SubverseModerators.Remove(subModerator);
            await _db.SaveChangesAsync();

            //clear mod cache
            CacheHandler.Instance.Remove(CachingKey.SubverseModerators(subverse.Name));

            return RedirectToAction("SubverseIndex", "Subverses", new { subversetoshow = subversetoresignfrom });
        }



        // GET: show subverse flair settings view for selected subverse
        [Authorize]
        public ActionResult SubverseFlairSettings(string subversetoshow)
        {
            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);

            if (subverseModel == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // check if caller is authorized for this sub, if not, deny listing
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.ModifyFlair))
            {
                return RedirectToAction("Index", "Home");
            }

            var subverseFlairsettings = _db.SubverseFlairs
                .Where(n => n.Subverse == subversetoshow)
                .Take(20)
                .OrderBy(s => s.ID)
                .ToList();

            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;

            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/Flair/FlairSettings.cshtml", subverseFlairsettings);
        }

        // GET: show add link flair view for selected subverse
        [Authorize]
        public ActionResult AddLinkFlair(string subversetoshow)
        {
            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);

            if (subverseModel == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            //check perms
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.ModifyFlair))
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;
            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/Flair/AddLinkFlair.cshtml");
        }

        // POST: add a link flair to given subverse
        [Authorize]
        [HttpPost]
        [VoatValidateAntiForgeryToken]
        public ActionResult AddLinkFlair([Bind(Include = "Id,Subverse,Label,CssClass")] SubverseFlair subverseFlairSetting)
        {
            if (!ModelState.IsValid)
            {
                return View(subverseFlairSetting);
            }

            //check perms
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subverseFlairSetting.Subverse, Domain.Models.ModeratorAction.ModifyFlair))
            {
                return RedirectToAction("Index", "Home");
            }

            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subverseFlairSetting.Subverse);
            if (subverseModel == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            subverseFlairSetting.Subverse = subverseModel.Name;
            _db.SubverseFlairs.Add(subverseFlairSetting);
            _db.SaveChanges();
            return RedirectToAction("SubverseFlairSettings");
        }

        // GET: show remove link flair view for selected subverse
        [Authorize]
        public ActionResult RemoveLinkFlair(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var subverseFlairSetting = _db.SubverseFlairs.Find(id);

            if (subverseFlairSetting == null)
            {
                return HttpNotFound();
            }

            ViewBag.SubverseName = subverseFlairSetting.Subverse;
            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/Flair/RemoveLinkFlair.cshtml", subverseFlairSetting);
        }

        // POST: remove a link flair from given subverse
        [Authorize]
        [HttpPost, ActionName("RemoveLinkFlair")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveLinkFlair(int id)
        {
            // get link flair for selected subverse
            var linkFlairToRemove = await _db.SubverseFlairs.FindAsync(id);
            if (linkFlairToRemove == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var subverse = DataCache.Subverse.Retrieve(linkFlairToRemove.Subverse);
            if (subverse == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            // check if caller has clearance to remove a link flair
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subverse.Name, Domain.Models.ModeratorAction.ModifyFlair))
            {
                return RedirectToAction("Index", "Home");
            }

            // execute removal
            var subverseFlairSetting = await _db.SubverseFlairs.FindAsync(id);
            _db.SubverseFlairs.Remove(subverseFlairSetting);
            await _db.SaveChangesAsync();
            return RedirectToAction("SubverseFlairSettings");
        }
        #region ADD/REMOVE MODERATORS LOGIC

        [HttpGet]
        [Authorize]
        public async Task<ActionResult> AcceptModInvitation(int invitationId)
        {
            int maximumOwnedSubs = Settings.MaximumOwnedSubs;

            //TODO: These errors are not friendly - please update to redirect or something
            // check if there is an invitation for this user with this id
            var userInvitation = _db.ModeratorInvitations.Find(invitationId);
            if (userInvitation == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // check if logged in user is actually the invited user
            if (!User.Identity.Name.Equals(userInvitation.Recipient, StringComparison.OrdinalIgnoreCase))
            {
                return new HttpStatusCodeResult(HttpStatusCode.Unauthorized);
            }

            // check if user is over modding limits
            var amountOfSubsUserModerates = _db.SubverseModerators.Where(s => s.UserName.Equals(User.Identity.Name, StringComparison.OrdinalIgnoreCase));
            if (amountOfSubsUserModerates.Any())
            {
                if (amountOfSubsUserModerates.Count() >= maximumOwnedSubs)
                {
                    ModelState.AddModelError(string.Empty, "Sorry, you can not own or moderate more than " + maximumOwnedSubs + " subverses.");
                    return RedirectToAction("Index", "Home");
                }
            }

            // check if subverse exists
            var subverseToAddModTo = _db.Subverses.FirstOrDefault(s => s.Name.Equals(userInvitation.Subverse, StringComparison.OrdinalIgnoreCase));
            if (subverseToAddModTo == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // check if user is already a moderator of this sub
            var userModerating = _db.SubverseModerators.Where(s => s.Subverse.Equals(userInvitation.Subverse, StringComparison.OrdinalIgnoreCase) && s.UserName.Equals(User.Identity.Name, StringComparison.OrdinalIgnoreCase));
            if (userModerating.Any())
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            // add user as moderator as specified in invitation
            var subAdm = new SubverseModerator
            {
                Subverse = subverseToAddModTo.Name,
                UserName = UserHelper.OriginalUsername(userInvitation.Recipient),
                Power = userInvitation.Power,
                CreatedBy = UserHelper.OriginalUsername(userInvitation.CreatedBy),
                CreationDate = Repository.CurrentDate
            };

            _db.SubverseModerators.Add(subAdm);

            // notify sender that user has accepted the invitation
            var message = new Domain.Models.SendMessage()
            {
                Sender = $"v/{subverseToAddModTo}",
                Subject = $"Moderator invitation for v/{userInvitation.Subverse} accepted",
                Recipient = userInvitation.CreatedBy,
                Message = $"User {User.Identity.Name} has accepted your invitation to moderate subverse v/{userInvitation.Subverse}."
            };
            var cmd = new SendMessageCommand(message);
            await cmd.Execute();

            //clear mod cache
            CacheHandler.Instance.Remove(CachingKey.SubverseModerators(userInvitation.Subverse));

            // delete the invitation from database
            _db.ModeratorInvitations.Remove(userInvitation);
            _db.SaveChanges();

            return RedirectToAction("SubverseSettings", "Subverses", new { subversetoshow = userInvitation.Subverse });
        }

        // GET: show add moderators view for selected subverse
        [Authorize]
        public ActionResult AddModerator(string subversetoshow)
        {
            // get model for selected subverse
            var subverseModel = DataCache.Subverse.Retrieve(subversetoshow);
            if (subverseModel == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subversetoshow, Domain.Models.ModeratorAction.InviteMods))
            {
                return RedirectToAction("Index", "Home");
            }

            ViewBag.SubverseModel = subverseModel;
            ViewBag.SubverseName = subversetoshow;
            ViewBag.SelectedSubverse = string.Empty;
            return View("~/Views/Subverses/Admin/AddModerator.cshtml");
        }

        // POST: add a moderator to given subverse
        [Authorize]
        [HttpPost]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> AddModerator([Bind(Include = "ID,Subverse,Username,Power")] SubverseModerator subverseAdmin)
        {
            if (!ModelState.IsValid)
            {
                return View(subverseAdmin);
            }

            // check if caller can add mods, if not, deny posting
            if (!ModeratorPermission.HasPermission(User.Identity.Name, subverseAdmin.Subverse, Domain.Models.ModeratorAction.InviteMods))
            {
                return RedirectToAction("Index", "Home");
            }

            subverseAdmin.UserName = subverseAdmin.UserName.TrimSafe();
            Subverse subverseModel = null;

            //lots of premature retuns so wrap the common code
            var sendFailureResult = new Func<string, ActionResult>(errorMessage =>
            {
                ViewBag.SubverseModel = subverseModel;
                ViewBag.SubverseName = subverseAdmin.Subverse;
                ViewBag.SelectedSubverse = string.Empty;
                ModelState.AddModelError(string.Empty, errorMessage);
                return View("~/Views/Subverses/Admin/AddModerator.cshtml",
                new SubverseModeratorViewModel
                {
                    UserName = subverseAdmin.UserName,
                    Power = subverseAdmin.Power
                });
            });

            // prevent invites to the current moderator
            if (User.Identity.Name.Equals(subverseAdmin.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return sendFailureResult("Can not add yourself as a moderator");
            }

            string originalRecipientUserName = UserHelper.OriginalUsername(subverseAdmin.UserName);
            // prevent invites to the current moderator
            if (String.IsNullOrEmpty(originalRecipientUserName))
            {
                return sendFailureResult("User can not be found");
            }

            // get model for selected subverse
            subverseModel = DataCache.Subverse.Retrieve(subverseAdmin.Subverse);
            if (subverseModel == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            if ((subverseAdmin.Power < 1 || subverseAdmin.Power > 4) && subverseAdmin.Power != 99)
            {
                return sendFailureResult("Only powers levels 1 - 4 and 99 are supported currently");
            }

            //check current mod level and invite level and ensure they are a lower level
            var currentModLevel = ModeratorPermission.Level(User.Identity.Name, subverseModel.Name);
            if (subverseAdmin.Power <= (int)currentModLevel && currentModLevel != Domain.Models.ModeratorLevel.Owner)
            {
                return sendFailureResult("Sorry, but you can only add moderators that are a lower level than yourself");
            }

            int maximumOwnedSubs = Settings.MaximumOwnedSubs;

            // check if the user being added is not already a moderator of 10 subverses
            var currentlyModerating = _db.SubverseModerators.Where(a => a.UserName == originalRecipientUserName).ToList();

            SubverseModeratorViewModel tmpModel;
            if (currentlyModerating.Count <= maximumOwnedSubs)
            {
                // check that user is not already moderating given subverse
                var isAlreadyModerator = _db.SubverseModerators.FirstOrDefault(a => a.UserName == originalRecipientUserName && a.Subverse == subverseAdmin.Subverse);

                if (isAlreadyModerator == null)
                {
                    // check if this user is already invited
                    var userModeratorInvitations = _db.ModeratorInvitations.Where(i => i.Recipient.Equals(originalRecipientUserName, StringComparison.OrdinalIgnoreCase) && i.Subverse.Equals(subverseModel.Name, StringComparison.OrdinalIgnoreCase));
                    if (userModeratorInvitations.Any())
                    {
                        return sendFailureResult("Sorry, the user is already invited to moderate this subverse");
                    }

                    // send a new moderator invitation
                    ModeratorInvitation modInv = new ModeratorInvitation
                    {
                        CreatedBy = User.Identity.Name,
                        CreationDate = Repository.CurrentDate,
                        Recipient = originalRecipientUserName,
                        Subverse = subverseAdmin.Subverse,
                        Power = subverseAdmin.Power
                    };

                    _db.ModeratorInvitations.Add(modInv);
                    _db.SaveChanges();

                    int invitationId = modInv.ID;
                    var invitationBody = new StringBuilder();
                    invitationBody.Append("Hello,");
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append($"@{User.Identity.Name} invited you to moderate v/" + subverseAdmin.Subverse + ".");
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append("Please visit the following link if you want to accept this invitation: " + "https://" + Request.ServerVariables["HTTP_HOST"] + "/acceptmodinvitation/" + invitationId);
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append(Environment.NewLine);
                    invitationBody.Append("Thank you.");

                    var cmd = new SendMessageCommand(new Domain.Models.SendMessage()
                    {
                        Sender = $"v/{subverseAdmin.Subverse}",
                        Recipient = originalRecipientUserName,
                        Subject = $"v/{subverseAdmin.Subverse} moderator invitation",
                        Message = invitationBody.ToString()
                    }, true);
                    await cmd.Execute();

                    return RedirectToAction("SubverseModerators");
                }
                else
                {
                    return sendFailureResult("Sorry, the user is already moderating this subverse");
                }
            }
            else
            {
                return sendFailureResult("Sorry, the user is already moderating a maximum of " + maximumOwnedSubs + " subverses");
            }
        }
        // GET: show remove moderators view for selected subverse
        [Authorize]
        public ActionResult RemoveModerator(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var subModerator = _db.SubverseModerators.Find(id);

            if (subModerator == null)
            {
                return HttpNotFound();
            }

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subModerator.Subverse, Domain.Models.ModeratorAction.RemoveMods))
            {
                return RedirectToAction("Index", "Home");
            }

            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.SubverseName = subModerator.Subverse;
            return View("~/Views/Subverses/Admin/RemoveModerator.cshtml", subModerator);
        }

        // POST: remove a moderator from given subverse
        [Authorize]
        [HttpPost, ActionName("RemoveModerator")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveModerator(int id)
        {

            var cmd = new RemoveModeratorCommand(id, true);
            var response = await cmd.Execute();

            if (response.Success)
            {
                return RedirectToAction("SubverseModerators");
            }
            else
            {
                ModelState.AddModelError("", response.Message);
                if (response.Response.SubverseModerator != null)
                {
                    var model = new SubverseModerator()
                    {
                        ID = response.Response.SubverseModerator.ID,
                        Subverse = response.Response.SubverseModerator.Subverse,
                        UserName = response.Response.SubverseModerator.UserName,
                        Power = response.Response.SubverseModerator.Power
                    };
                    return View("~/Views/Subverses/Admin/RemoveModerator.cshtml", model);
                }
                else
                {
                    //bail
                    return RedirectToAction("SubverseModerators");
                }
            }
        }

        #endregion ADD/REMOVE MODERATORS LOGIC

       
    }
}