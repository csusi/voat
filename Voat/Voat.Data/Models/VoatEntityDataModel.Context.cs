﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Voat.Data.Models
{
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    using System.Data.Entity.Core.Objects;
    using System.Linq;
    
    public partial class voatEntities : DbContext
    {
        public voatEntities()
            : base("name=voatEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<Badge> Badges { get; set; }
        public virtual DbSet<Banneddomain> Banneddomains { get; set; }
        public virtual DbSet<Banneduser> Bannedusers { get; set; }
        public virtual DbSet<CommentRemovalLog> CommentRemovalLogs { get; set; }
        public virtual DbSet<Commentreplynotification> Commentreplynotifications { get; set; }
        public virtual DbSet<Comment> Comments { get; set; }
        public virtual DbSet<Commentsavingtracker> Commentsavingtrackers { get; set; }
        public virtual DbSet<Commentvotingtracker> Commentvotingtrackers { get; set; }
        public virtual DbSet<Defaultsubverse> Defaultsubverses { get; set; }
        public virtual DbSet<Featuredsub> Featuredsubs { get; set; }
        public virtual DbSet<Message> Messages { get; set; }
        public virtual DbSet<Moderatorinvitation> Moderatorinvitations { get; set; }
        public virtual DbSet<PartnerInformation> PartnerInformations { get; set; }
        public virtual DbSet<Postreplynotification> Postreplynotifications { get; set; }
        public virtual DbSet<Privatemessage> Privatemessages { get; set; }
        public virtual DbSet<Promotedsubmission> Promotedsubmissions { get; set; }
        public virtual DbSet<Savingtracker> Savingtrackers { get; set; }
        public virtual DbSet<Session> Sessions { get; set; }
        public virtual DbSet<Sessiontracker> Sessiontrackers { get; set; }
        public virtual DbSet<Stickiedsubmission> Stickiedsubmissions { get; set; }
        public virtual DbSet<SubmissionRemovalLog> SubmissionRemovalLogs { get; set; }
        public virtual DbSet<Subscription> Subscriptions { get; set; }
        public virtual DbSet<SubverseAdmin> SubverseAdmins { get; set; }
        public virtual DbSet<SubverseBan> SubverseBans { get; set; }
        public virtual DbSet<Subverseflairsetting> Subverseflairsettings { get; set; }
        public virtual DbSet<Subverse> Subverses { get; set; }
        public virtual DbSet<Userbadge> Userbadges { get; set; }
        public virtual DbSet<UserBlockedSubverse> UserBlockedSubverses { get; set; }
        public virtual DbSet<Userpreference> Userpreferences { get; set; }
        public virtual DbSet<Userscore> Userscores { get; set; }
        public virtual DbSet<Usersetdefinition> Usersetdefinitions { get; set; }
        public virtual DbSet<Userset> Usersets { get; set; }
        public virtual DbSet<Usersetsubscription> Usersetsubscriptions { get; set; }
        public virtual DbSet<Viewstatistic> Viewstatistics { get; set; }
        public virtual DbSet<Votingtracker> Votingtrackers { get; set; }
        public virtual DbSet<AutoModComment> AutoModComments { get; set; }
        public virtual DbSet<AutoModSubmission> AutoModSubmissions { get; set; }
    
        public virtual ObjectResult<usp_CommentTree_Result> usp_CommentTree(Nullable<int> submissionID, Nullable<int> depth, Nullable<int> parentID)
        {
            var submissionIDParameter = submissionID.HasValue ?
                new ObjectParameter("SubmissionID", submissionID) :
                new ObjectParameter("SubmissionID", typeof(int));
    
            var depthParameter = depth.HasValue ?
                new ObjectParameter("Depth", depth) :
                new ObjectParameter("Depth", typeof(int));
    
            var parentIDParameter = parentID.HasValue ?
                new ObjectParameter("ParentID", parentID) :
                new ObjectParameter("ParentID", typeof(int));
    
            return ((IObjectContextAdapter)this).ObjectContext.ExecuteFunction<usp_CommentTree_Result>("usp_CommentTree", submissionIDParameter, depthParameter, parentIDParameter);
        }
    }
}
