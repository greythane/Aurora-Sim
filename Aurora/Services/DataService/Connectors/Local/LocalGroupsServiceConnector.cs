/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Framework;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using log4net;

namespace Aurora.Services.DataService
{
    public class LocalGroupsServiceConnector : IGroupsServiceConnector
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IGenericData data;

        #region IGroupsServiceConnector Members

        public void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore simBase,
                               string defaultConnectionString)
        {
            data = GenericData;

            if (source.Configs[Name] != null)
                defaultConnectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

            data.ConnectToDatabase(defaultConnectionString, "Groups",
                                   source.Configs["AuroraConnectors"].GetBoolean("ValidateTables", true));

            DataManager.DataManager.RegisterPlugin(Name + "Local", this);

            if (source.Configs["AuroraConnectors"].GetString("GroupsConnector", "LocalConnector") == "LocalConnector")
            {
                DataManager.DataManager.RegisterPlugin(Name, this);
            }
        }

        public string Name
        {
            get { return "IGroupsServiceConnector"; }
        }

        public void CreateGroup(UUID groupID, string name, string charter, bool showInList, UUID insigniaID,
                                int membershipFee, bool openEnrollment, bool allowPublish, bool maturePublish,
                                UUID founderID,
                                ulong EveryonePowers, UUID OwnerRoleID, ulong OwnerPowers)
        {
            List<string> Keys = new List<string>
                                    {
                                        "GroupID",
                                        "Name",
                                        "Charter",
                                        "InsigniaID",
                                        "FounderID",
                                        "MembershipFee",
                                        "OpenEnrollment",
                                        "ShowInList",
                                        "AllowPublish",
                                        "MaturePublish",
                                        "OwnerRoleID"
                                    };
            List<Object> Values = new List<object>
                                      {
                                          groupID,
                                          name.MySqlEscape(),
                                          charter.MySqlEscape(),
                                          insigniaID,
                                          founderID,
                                          membershipFee,
                                          openEnrollment ? 1 : 0,
                                          showInList ? 1 : 0,
                                          allowPublish ? 1 : 0,
                                          maturePublish ? 1 : 0,
                                          OwnerRoleID
                                      };
            data.Insert("osgroup", Keys.ToArray(), Values.ToArray());

            //Add everyone role to group
            AddRoleToGroup(founderID, groupID, UUID.Zero, "Everyone", "Everyone in the group is in the everyone role.",
                           "Member of " + name, EveryonePowers);

            ulong groupPowers = 296868139497678;

            UUID officersRole = UUID.Random();
            //Add officers role to group
            AddRoleToGroup(founderID, groupID, officersRole, "Officers",
                           "The officers of the group, with more powers than regular members.", "Officer of " + name,
                           groupPowers);

            //Add owner role to group
            AddRoleToGroup(founderID, groupID, OwnerRoleID, "Owners", "Owners of " + name, "Owner of " + name,
                           OwnerPowers);

            //Add owner to the group as owner
            AddAgentToGroup(founderID, founderID, groupID, OwnerRoleID);

            SetAgentGroupSelectedRole(founderID, groupID, OwnerRoleID);

            SetAgentActiveGroup(founderID, groupID);
        }

        public string SetAgentActiveGroup(UUID AgentID, UUID GroupID)
        {
            if (data.Query("AgentID", AgentID, "osagent", "*").Count != 0)
                data.Update("osagent", new object[] {GroupID}, new[] {"ActiveGroupID"}, new[] {"AgentID"},
                            new object[] {AgentID});
            else
                data.Insert("osagent", new[]
                                           {
                                               "AgentID",
                                               "ActiveGroupID"
                                           }, new object[]
                                                  {
                                                      AgentID,
                                                      GroupID
                                                  });
            GroupMembersData gdata = GetAgentGroupMemberData(AgentID, GroupID, AgentID);
            return gdata == null ? "" : gdata.Title;
        }

        public string SetAgentGroupSelectedRole(UUID AgentID, UUID GroupID, UUID RoleID)
        {
            data.Update("osgroupmembership", new object[] {RoleID}, new[] {"SelectedRoleID"}, new[]
                                                                                                  {
                                                                                                      "AgentID",
                                                                                                      "GroupID"
                                                                                                  }, new object[]
                                                                                                         {
                                                                                                             AgentID,
                                                                                                             GroupID
                                                                                                         });
            GroupMembersData gdata = GetAgentGroupMemberData(AgentID, GroupID, AgentID);
            return gdata == null ? "" : gdata.Title;
        }

        public void AddAgentToGroup(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            if (data.Query(new[]
                               {
                                   "AgentID",
                                   "GroupID"
                               }, new object[]
                                      {
                                          AgentID,
                                          GroupID
                                      }, "osgroupmembership", "*").Count != 0)
            {
                m_log.Error("[AGM]: Agent " + AgentID + " is already in " + GroupID);
                return;
            }
            else
            {
                List<string> Keys = new List<string>
                                        {
                                            "GroupID",
                                            "AgentID",
                                            "SelectedRoleID",
                                            "Contribution",
                                            "ListInProfile",
                                            "AcceptNotices"
                                        };
                List<Object> Values = new List<object> {GroupID, AgentID, RoleID, 0, 1, 1};
                data.Insert("osgroupmembership", Keys.ToArray(), Values.ToArray());
            }

            // Make sure they're in the Everyone role
            AddAgentToRole(requestingAgentID, AgentID, GroupID, UUID.Zero);
            // Make sure they're in specified role, if they were invited
            if (RoleID != UUID.Zero)
                AddAgentToRole(requestingAgentID, AgentID, GroupID, RoleID);
            //Set the role they were invited to as their selected role
            SetAgentGroupSelectedRole(AgentID, GroupID, RoleID);
            SetAgentActiveGroup(AgentID, GroupID);
        }

        public void AddRoleToGroup(UUID requestingAgentID, UUID GroupID, UUID RoleID, string Name, string Description,
                                   string Title, ulong Powers)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.CreateRole))
                return;
            List<string> Keys = new List<string> {"GroupID", "RoleID", "Name", "Description", "Title", "Powers"};
            List<Object> Values = new List<object>
                                      {GroupID, RoleID, Name.MySqlEscape(), Description.MySqlEscape(), Title, Powers};
            data.Insert("osrole", Keys.ToArray(), Values.ToArray());
        }

        public void UpdateGroup(UUID requestingAgentID, UUID groupID, string charter, int showInList, UUID insigniaID,
                                int membershipFee, int openEnrollment, int allowPublish, int maturePublish)
        {
            if (
                !CheckGroupPermissions(requestingAgentID, groupID,
                                       (ulong) (GroupPowers.ChangeOptions | GroupPowers.ChangeIdentity)))
                return;
            data.Update("osgroup", new object[]
                                       {
                                           charter.MySqlEscape(),
                                           insigniaID,
                                           membershipFee,
                                           openEnrollment,
                                           showInList,
                                           allowPublish,
                                           maturePublish
                                       }, new[]
                                              {
                                                  "Charter",
                                                  "InsigniaID",
                                                  "MembershipFee",
                                                  "OpenEnrollment",
                                                  "ShowInList",
                                                  "AllowPublish",
                                                  "MaturePublish"
                                              }, new[] {"GroupID"}, new object[] {groupID});
        }

        public void RemoveRoleFromGroup(UUID requestingAgentID, UUID RoleID, UUID GroupID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.DeleteRole))
                return;
            data.Delete("osgrouprolemembership", new[]
                                                     {
                                                         "GroupID",
                                                         "RoleID"
                                                     }, new object[]
                                                            {
                                                                GroupID,
                                                                RoleID
                                                            });
            data.Update("osgroupmembership", new object[] {UUID.Zero}, new[] {"SelectedRoleID"}, new[]
                                                                                                     {
                                                                                                         "GroupID",
                                                                                                         "SelectedRoleID"
                                                                                                     }, new object[]
                                                                                                            {
                                                                                                                GroupID,
                                                                                                                RoleID
                                                                                                            });
            data.Delete("osrole", new[]
                                      {
                                          "GroupID",
                                          "RoleID"
                                      }, new object[]
                                             {
                                                 GroupID,
                                                 RoleID
                                             });
        }

        public void UpdateRole(UUID requestingAgentID, UUID GroupID, UUID RoleID, string Name, string Desc, string Title,
                               ulong Powers)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.RoleProperties))
                return;
            List<string> Keys = new List<string> {"RoleID"};
            if (Name != null)
                Keys.Add("Name");
            if (Desc != null)
                Keys.Add("Description");
            if (Title != null)
                Keys.Add("Title");

            Keys.Add("Powers");

            List<object> Values = new List<object> {RoleID};
            if (Name != null)
                Values.Add(Name.MySqlEscape());
            if (Desc != null)
                Values.Add(Desc.MySqlEscape());
            if (Title != null)
                Values.Add(Title.MySqlEscape());

            Values.Add(Powers);

            data.Update("osrole", Values.ToArray(), Keys.ToArray(), new[]
                                                                        {
                                                                            "GroupID",
                                                                            "RoleID"
                                                                        }, new object[]
                                                                               {
                                                                                   GroupID,
                                                                                   RoleID
                                                                               });
        }

        public GroupRecord GetGroupRecord(UUID requestingAgentID, UUID GroupID, string GroupName)
        {
            GroupRecord record = new GroupRecord();

            List<string> Keys = new List<string>();
            List<object> Values = new List<object>();
            if (GroupID != UUID.Zero)
            {
                Keys.Add("GroupID");
                Values.Add(GroupID);
            }
            if (!string.IsNullOrEmpty(GroupName))
            {
                Keys.Add("Name");
                Values.Add(GroupName.MySqlEscape());
            }
            List<string> osgroupsData = data.Query(Keys.ToArray(), Values.ToArray(), "osgroup",
                                                   "GroupID, Name, Charter, InsigniaID, FounderID, MembershipFee, OpenEnrollment, ShowInList, AllowPublish, MaturePublish, OwnerRoleID");
            if (osgroupsData.Count == 0)
                return null;
            record.GroupID = UUID.Parse(osgroupsData[0]);
            record.GroupName = osgroupsData[1];
            record.Charter = osgroupsData[2];
            record.GroupPicture = UUID.Parse(osgroupsData[3]);
            record.FounderID = UUID.Parse(osgroupsData[4]);
            record.MembershipFee = int.Parse(osgroupsData[5]);
            record.OpenEnrollment = int.Parse(osgroupsData[6]) == 1;
            record.ShowInList = int.Parse(osgroupsData[7]) == 1;
            record.AllowPublish = int.Parse(osgroupsData[8]) == 1;
            record.MaturePublish = int.Parse(osgroupsData[9]) == 1;
            record.OwnerRoleID = UUID.Parse(osgroupsData[10]);

            return record;
        }

        public GroupProfileData GetMemberGroupProfile(UUID requestingAgentID, UUID GroupID, UUID AgentID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.MemberVisible))
                return new GroupProfileData();

            GroupProfileData GPD = new GroupProfileData();
            GroupRecord record = GetGroupRecord(requestingAgentID, GroupID, null);
            List<string> Membership = data.Query(new[]
                                                     {
                                                         "GroupID",
                                                         "AgentID"
                                                     }, new object[]
                                                            {
                                                                AgentID,
                                                                GroupID
                                                            }, "osgroupmembership",
                                                 "Contribution, ListInProfile, SelectedRoleID");
            List<string> GroupMemCount = data.Query(new[] {"GroupID"}, new object[] {GroupID}, "osgroupmembership",
                                                    "count(AgentID)");
            List<string> GroupRoleCount = data.Query(new[] {"GroupID"}, new object[] {GroupID}, "osrole",
                                                     "count(RoleID)");
            List<string> GroupRole = data.Query(new[] {"RoleID"}, new object[] {Membership[2]}, "osrole", "Name, Powers");

            GPD.AllowPublish = record.AllowPublish;
            GPD.Charter = record.Charter;
            GPD.FounderID = record.FounderID;
            GPD.GroupID = record.GroupID;
            GPD.GroupMembershipCount = int.Parse(GroupMemCount[0]);
            GPD.GroupRolesCount = int.Parse(GroupRoleCount[0]);
            GPD.InsigniaID = record.GroupPicture;
            GPD.MaturePublish = record.MaturePublish;
            GPD.MembershipFee = record.MembershipFee;
            GPD.MemberTitle = GroupRole[0];
            GPD.Money = 0;

            GPD.Name = record.GroupName;
            GPD.OpenEnrollment = record.OpenEnrollment;
            GPD.OwnerRole = record.OwnerRoleID;
            GPD.PowersMask = ulong.Parse(GroupRole[1]);
            GPD.ShowInList = int.Parse(Membership[2]) == 1;

            return GPD;
        }

        public GroupMembershipData GetGroupMembershipData(UUID requestingAgentID, UUID GroupID, UUID AgentID)
        {
            GroupMembershipData GMD = new GroupMembershipData();
            GroupRecord record = GetGroupRecord(requestingAgentID, GroupID, null);
            List<string> Membership = data.Query(new[]
                                                     {
                                                         "GroupID",
                                                         "AgentID"
                                                     }, new object[]
                                                            {
                                                                GroupID,
                                                                AgentID
                                                            }, "osgroupmembership",
                                                 "AcceptNotices, Contribution, ListInProfile, SelectedRoleID");
            if (Membership.Count == 0)
                return null;

            List<string> GroupRole = data.Query(new[] {"GroupID", "RoleID"}, new object[] {GroupID, Membership[3]},
                                                "osrole", "Title, Powers");
            if (GroupRole.Count == 0)
                return null;
            GMD.AcceptNotices = int.Parse(Membership[0]) == 1;
            //TODO: Figure out what this is and its effects if false
            GMD.Active = true;
            GMD.ActiveRole = UUID.Parse(Membership[3]);
            GMD.AllowPublish = record.AllowPublish;
            GMD.Charter = record.Charter;
            GMD.Contribution = int.Parse(Membership[1]);
            GMD.FounderID = record.FounderID;
            GMD.GroupID = record.GroupID;
            GMD.GroupName = record.GroupName;
            GMD.GroupPicture = record.GroupPicture;
            GMD.GroupPowers = ulong.Parse(GroupRole[1]);
            GMD.GroupTitle = GroupRole[0];
            GMD.ListInProfile = int.Parse(Membership[2]) == 1;
            GMD.MaturePublish = record.MaturePublish;
            GMD.MembershipFee = record.MembershipFee;
            GMD.OpenEnrollment = record.OpenEnrollment;
            GMD.ShowInList = record.ShowInList;

            return GMD;
        }

        public List<GroupMembershipData> GetAgentGroupMemberships(UUID requestingAgentID, UUID AgentID)
        {
            List<string> Groups = data.Query(new[]
                                                 {
                                                     "AgentID"
                                                 }, new object[]
                                                        {
                                                            AgentID
                                                        }, "osgroupmembership", "GroupID");
            return Groups.Select(GroupID => GetGroupMembershipData(requestingAgentID, UUID.Parse(GroupID), AgentID)).ToList();
        }

        public void SetAgentGroupInfo(UUID requestingAgentID, UUID AgentID, UUID GroupID, int AcceptNotices,
                                      int ListInProfile)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.ChangeIdentity))
                return;

            data.Update("osgroupmembership", new object[]
                                                 {
                                                     AgentID,
                                                     AcceptNotices,
                                                     ListInProfile
                                                 }, new[]
                                                        {
                                                            "AgentID",
                                                            "AcceptNotices",
                                                            "ListInProfile"
                                                        }, new[]
                                                               {
                                                                   "GroupID",
                                                                   "AgentID"
                                                               }, new object[]
                                                                      {
                                                                          AgentID,
                                                                          GroupID
                                                                      });
        }

        public void AddAgentGroupInvite(UUID requestingAgentID, UUID inviteID, UUID GroupID, UUID roleID, UUID AgentID,
                                        string FromAgentName)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.Invite))
                return;
            data.Delete("osgroupinvite", new[]
                                             {
                                                 "AgentID",
                                                 "GroupID"
                                             }, new object[]
                                                    {
                                                        AgentID,
                                                        GroupID
                                                    });
            data.Insert("osgroupinvite", new[]
                                             {
                                                 "InviteID",
                                                 "GroupID",
                                                 "RoleID",
                                                 "AgentID",
                                                 "TMStamp",
                                                 "FromAgentName"
                                             }, new object[]
                                                    {
                                                        inviteID,
                                                        GroupID,
                                                        roleID,
                                                        AgentID,
                                                        Util.UnixTimeSinceEpoch(),
                                                        FromAgentName.MySqlEscape()
                                                    });
        }

        public GroupInviteInfo GetAgentToGroupInvite(UUID requestingAgentID, UUID inviteID)
        {
            GroupInviteInfo invite = new GroupInviteInfo();

            List<string> groupInvite = data.Query(new[]
                                                      {
                                                          "AgentID",
                                                          "InviteID"
                                                      }, new object[]
                                                             {
                                                                 requestingAgentID,
                                                                 inviteID
                                                             }, "osgroupinvite", "*");
            if (groupInvite.Count == 0)
                return null;
            invite.AgentID = UUID.Parse(groupInvite[3]);
            invite.GroupID = UUID.Parse(groupInvite[1]);
            invite.InviteID = UUID.Parse(groupInvite[0]);
            invite.RoleID = UUID.Parse(groupInvite[2]);
            invite.FromAgentName = groupInvite[5];

            return invite;
        }

        public List<GroupInviteInfo> GetGroupInvites(UUID requestingAgentID)
        {
            List<string> groupInvite = data.Query(new[]
                                                      {
                                                          "AgentID"
                                                      }, new object[]
                                                             {
                                                                 requestingAgentID
                                                             }, "osgroupinvite", "*");
            List<GroupInviteInfo> invites = new List<GroupInviteInfo>();

            for (int i = 0; i < groupInvite.Count; i += 6)
            {
                GroupInviteInfo invite = new GroupInviteInfo
                                             {
                                                 AgentID = UUID.Parse(groupInvite[i + 3]),
                                                 GroupID = UUID.Parse(groupInvite[i + 1]),
                                                 InviteID = UUID.Parse(groupInvite[i]),
                                                 RoleID = UUID.Parse(groupInvite[i + 2]),
                                                 FromAgentName = groupInvite[i + 5]
                                             };
                invites.Add(invite);
            }

            return invites;
        }

        public void RemoveAgentInvite(UUID requestingAgentID, UUID inviteID)
        {
            data.Delete("osgroupinvite", new[] {"InviteID"}, new object[] {inviteID});
        }

        public bool RemoveAgentFromGroup(UUID requestingAgentID, UUID AgentID, UUID GroupID)
        {
            if ((!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.RemoveMember)) &&
                (requestingAgentID != AgentID)) //Allow kicking yourself
                return false;
            // 1. If group is agent's active group, change active group to uuidZero
            // 2. Remove Agent from group (osgroupmembership)
            // 3. Remove Agent from all of the groups roles (osgrouprolemembership)
            data.Update("osagent", new object[] {UUID.Zero}, new[] {"ActiveGroupID"}, new[]
                                                                                          {
                                                                                              "AgentID",
                                                                                              "ActiveGroupID"
                                                                                          }, new object[]
                                                                                                 {
                                                                                                     AgentID,
                                                                                                     GroupID
                                                                                                 });

            data.Delete("osgrouprolemembership", new[]
                                                     {
                                                         "AgentID",
                                                         "GroupID"
                                                     }, new object[]
                                                            {
                                                                AgentID,
                                                                GroupID
                                                            });
            data.Delete("osgroupmembership", new[]
                                                 {
                                                     "AgentID",
                                                     "GroupID"
                                                 }, new object[]
                                                        {
                                                            AgentID,
                                                            GroupID
                                                        });

            return true;
        }

        public void AddAgentToRole(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.AssignMember))
            {
                //This isn't an open and shut case, they could be setting the agent to their role, which would allow for AssignMemberLimited
                if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.AssignMemberLimited))
                {
                    m_log.Warn("[AGM]: User " + requestingAgentID + " attempted to add user " + AgentID +
                               " to group " + GroupID + ", but did not have permissions to do so!");
                    return;
                }
            }

            List<string> query = data.Query(new[]
                                                {
                                                    "AgentID",
                                                    "RoleID",
                                                    "GroupID"
                                                }, new object[]
                                                       {
                                                           AgentID,
                                                           RoleID,
                                                           GroupID
                                                       }, "osgrouprolemembership", "count(AgentID)");
            //Make sure they arn't already in this role
            if (query[0] == "0")
            {
                data.Insert("osgrouprolemembership", new[]
                                                         {
                                                             "GroupID",
                                                             "RoleID",
                                                             "AgentID"
                                                         }, new object[]
                                                                {
                                                                    GroupID,
                                                                    RoleID,
                                                                    AgentID
                                                                });
            }
        }

        public void RemoveAgentFromRole(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.AssignMember))
                return;
            data.Update("osgroupmembership", new object[] {UUID.Zero}, new[] {"SelectedRoleID"}, new[]
                                                                                                     {
                                                                                                         "AgentID",
                                                                                                         "GroupID",
                                                                                                         "SelectedRoleID"
                                                                                                     }, new object[]
                                                                                                            {
                                                                                                                AgentID,
                                                                                                                GroupID,
                                                                                                                RoleID
                                                                                                            });
            data.Delete("osgrouprolemembership", new[]
                                                     {
                                                         "AgentID",
                                                         "GroupID",
                                                         "RoleID"
                                                     }, new object[]
                                                            {
                                                                AgentID,
                                                                GroupID,
                                                                RoleID
                                                            });
        }

        public List<DirGroupsReplyData> FindGroups(UUID requestingAgentID, string search, int StartQuery,
                                                   uint queryflags)
        {
            string whereClause = " Name LIKE '%" + search.MySqlEscape() + "%' LIMIT " + StartQuery + ",50 ";
            List<string> retVal = data.Query(whereClause, "osgroup",
                                             "GroupID,Name,ShowInList,AllowPublish,MaturePublish");

            List<DirGroupsReplyData> Reply = new List<DirGroupsReplyData>();
            DirGroupsReplyData dirgroup = new DirGroupsReplyData();
            for (int i = 0; i < retVal.Count; i += 5)
            {
                if (retVal[i + 2] == "0") // (ShowInList param) They don't want to be shown in search.. respect this
                    continue;

                if ((queryflags & (uint) DirectoryManager.DirFindFlags.IncludeMature) !=
                    (uint) DirectoryManager.DirFindFlags.IncludeMature)
                    if (retVal[i + 4] == "1") // (MaturePublish param) Check for pg,mature
                        continue;
                dirgroup.groupID = UUID.Parse(retVal[i]);
                dirgroup.groupName = retVal[i + 1];
                dirgroup.members =
                    int.Parse(
                        data.Query(new[] {"GroupID"}, new object[] {dirgroup.groupID}, "osgroupmembership",
                                   "count(AgentID)")[0]);
                Reply.Add(dirgroup);
                dirgroup = new DirGroupsReplyData();
            }
            return Reply;
        }

        public UUID GetAgentActiveGroup(UUID RequestingAgentID, UUID AgentID)
        {
            List<string> groups = data.Query("AgentID", AgentID, "osagent", "ActiveGroupID");
            if (groups.Count != 0)
                return UUID.Parse(groups[0]);
            else
                return UUID.Zero;
        }

        public List<GroupRolesData> GetAgentGroupRoles(UUID requestingAgentID, UUID AgentID, UUID GroupID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.None))
                return new List<GroupRolesData>();
            List<string> RoleIDs = data.Query(new[]
                                                  {
                                                      "AgentID",
                                                      "GroupID"
                                                  }, new object[]
                                                         {
                                                             AgentID,
                                                             GroupID
                                                         }, "osgrouprolemembership", "RoleID");

            return (from RoleID in RoleIDs
                    let Role = data.Query("RoleID", RoleID, "osrole", "Name,Description,Title,Powers")
                    select new GroupRolesData
                               {
                                   RoleID = UUID.Parse(RoleID), Name = Role[0], Description = Role[1], Powers = ulong.Parse(Role[3]), Title = Role[2]
                               }).ToList();
        }

        public List<GroupRolesData> GetGroupRoles(UUID requestingAgentID, UUID GroupID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.None))
                return new List<GroupRolesData>();
            List<GroupRolesData> GroupRoles = new List<GroupRolesData>();
            List<string> Roles = data.Query("GroupID", GroupID, "osrole", "Name,Description,Title,Powers,RoleID");
            for (int i = 0; i < Roles.Count; i += 5)
            {
                List<string> Count = data.Query(new[]
                                                    {
                                                        "GroupID",
                                                        "RoleID"
                                                    }, new object[]
                                                           {
                                                               GroupID,
                                                               UUID.Parse(Roles[i + 4])
                                                           }, "osgrouprolemembership", "count(AgentID)");
                GroupRolesData roledata = new GroupRolesData
                                              {
                                                  Members = int.Parse(Count[0]),
                                                  RoleID = UUID.Parse(Roles[i + 4]),
                                                  Name = Roles[i + 0],
                                                  Description = Roles[i + 1],
                                                  Powers = ulong.Parse(Roles[i + 3]),
                                                  Title = Roles[i + 2]
                                              };
                GroupRoles.Add(roledata);
            }
            return GroupRoles;
        }

        public List<GroupMembersData> GetGroupMembers(UUID requestingAgentID, UUID GroupID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.None))
                return new List<GroupMembersData>();

            List<string> Agents = data.Query("GroupID", GroupID, "osgroupmembership", "AgentID");
            return Agents.Select(Agent => GetAgentGroupMemberData(requestingAgentID, GroupID, UUID.Parse(Agent))).ToList();
        }

        public GroupMembersData GetAgentGroupMemberData(UUID requestingAgentID, UUID GroupID, UUID AgentID)
        {
            //Permissions
            List<string> OtherPermiss = data.Query(new[]
                                                       {
                                                           "GroupID",
                                                           "AgentID"
                                                       }, new object[]
                                                              {
                                                                  GroupID,
                                                                  requestingAgentID
                                                              }, "osgroupmembership",
                                                   "AcceptNotices, Contribution, ListInProfile, SelectedRoleID");
            if (OtherPermiss.Count == 0)
                return null;

            List<string> Membership = data.Query(new[]
                                                     {
                                                         "GroupID",
                                                         "AgentID"
                                                     }, new object[]
                                                            {
                                                                GroupID,
                                                                AgentID
                                                            }, "osgroupmembership",
                                                 "AcceptNotices, Contribution, ListInProfile, SelectedRoleID");
            if (Membership.Count == 0)
                return null;
            List<string> GroupRole = data.Query(new[] {"RoleID"}, new object[] {Membership[3]}, "osrole",
                                                "Title, Powers");
            if (GroupRole.Count == 0)
                return null;
            List<string> OwnerRoleID = data.Query(new[] {"GroupID"}, new object[] {GroupID}, "osgroup", "OwnerRoleID");
            bool IsOwner = data.Query(new[]
                                          {
                                              "GroupID",
                                              "RoleID",
                                              "AgentID"
                                          }, new object[]
                                                 {
                                                     GroupID,
                                                     OwnerRoleID[0],
                                                     AgentID
                                                 }, "osgrouprolemembership", "count(AgentID)")[0] != "0";

            GroupMembersData GMD = new GroupMembersData
                                       {
                                           AcceptNotices = (Membership[0]) == "1",
                                           AgentID = AgentID,
                                           Contribution = int.Parse(Membership[1]),
                                           IsOwner = IsOwner,
                                           ListInProfile = (Membership[2]) == "1",
                                           AgentPowers = ulong.Parse(GroupRole[1]),
                                           Title = GroupRole[0],
                                           OnlineStatus = "(Online)"
                                       };


            return GMD;
        }

        public List<GroupRoleMembersData> GetGroupRoleMembers(UUID requestingAgentID, UUID GroupID)
        {
            List<GroupRoleMembersData> RoleMembers = new List<GroupRoleMembersData>();
            List<string> Roles = data.Query("GroupID", GroupID, "osgrouprolemembership", "RoleID,AgentID");
            GroupMembersData GMD = GetAgentGroupMemberData(requestingAgentID, GroupID, requestingAgentID);
            for (int i = 0; i < Roles.Count; i += 2)
            {
                GroupRoleMembersData RoleMember = new GroupRoleMembersData
                                                      {
                                                          RoleID = UUID.Parse(Roles[i]),
                                                          MemberID = UUID.Parse(Roles[i + 1])
                                                      };
                List<string> roleInfo = data.Query("RoleID", RoleMember.RoleID, "osrole", "Powers");
                long canViewMemebersBit = 140737488355328L;
                long canDoBit = long.Parse(roleInfo[0]);
                // if they are a member, they can see everyone, otherwise, only the roles that are supposed to be shown
                if (GMD != null ||
                    ((canDoBit & canViewMemebersBit) == canViewMemebersBit || RoleMember.MemberID == requestingAgentID))
                    RoleMembers.Add(RoleMember);
            }

            return RoleMembers;
        }

        public GroupNoticeInfo GetGroupNotice(UUID requestingAgentID, UUID noticeID)
        {
            List<string> notice = data.Query("NoticeID", noticeID, "osgroupnotice",
                                             "GroupID,Timestamp,FromName,Subject,ItemID,HasAttachment,Message,AssetType,ItemName");
            GroupNoticeData GND = new GroupNoticeData
                                      {
                                          NoticeID = noticeID,
                                          Timestamp = uint.Parse(notice[1]),
                                          FromName = notice[2],
                                          Subject = notice[3],
                                          HasAttachment = int.Parse(notice[5]) == 1
                                      };
            if (GND.HasAttachment)
            {
                GND.ItemID = UUID.Parse(notice[4]);
                GND.AssetType = (byte) int.Parse(notice[7]);
                GND.ItemName = notice[8];
            }

            GroupNoticeInfo info = new GroupNoticeInfo
                                       {
                                           BinaryBucket = new byte[0],
                                           GroupID = UUID.Parse(notice[0]),
                                           Message = notice[6],
                                           noticeData = GND
                                       };

            if (!CheckGroupPermissions(requestingAgentID, info.GroupID, (ulong) GroupPowers.ReceiveNotices))
                return null;
            return info;
        }

        public List<GroupNoticeData> GetGroupNotices(UUID requestingAgentID, UUID GroupID)
        {
            if (!CheckGroupPermissions(requestingAgentID, GroupID, (ulong) GroupPowers.ReceiveNotices))
                return new List<GroupNoticeData>();
            List<GroupNoticeData> AllNotices = new List<GroupNoticeData>();
            List<string> notice = data.Query("GroupID", GroupID, "osgroupnotice",
                                             "GroupID,Timestamp,FromName,Subject,ItemID,HasAttachment,NoticeID,Message,AssetType,ItemName");
            for (int i = 0; i < notice.Count; i += 10)
            {
                GroupNoticeData GND = new GroupNoticeData
                                          {
                                              NoticeID = UUID.Parse(notice[i + 6]),
                                              Timestamp = uint.Parse(notice[i + 1]),
                                              FromName = notice[i + 2],
                                              Subject = notice[i + 3],
                                              HasAttachment = int.Parse(notice[i + 5]) == 1
                                          };
                if (GND.HasAttachment)
                {
                    GND.ItemID = UUID.Parse(notice[i + 4]);
                    GND.AssetType = (byte) int.Parse(notice[i + 8]);
                    GND.ItemName = notice[i + 9];
                }

                AllNotices.Add(GND);
            }
            return AllNotices;
        }

        public void AddGroupNotice(UUID requestingAgentID, UUID groupID, UUID noticeID, string fromName, string subject,
                                   string message, UUID ItemID, int AssetType, string ItemName)
        {
            if (!CheckGroupPermissions(requestingAgentID, groupID, (ulong) GroupPowers.SendNotices))
                return;
            List<string> Keys = new List<string>
                                    {
                                        "GroupID",
                                        "NoticeID",
                                        "Timestamp",
                                        "FromName",
                                        "Subject",
                                        "Message",
                                        "HasAttachment",
                                        "ItemID",
                                        "AssetType",
                                        "ItemName"
                                    };

            List<object> Values = new List<object>
                                      {
                                          groupID,
                                          noticeID,
                                          ((uint) Util.UnixTimeSinceEpoch()),
                                          fromName.MySqlEscape(),
                                          subject.MySqlEscape(),
                                          message.MySqlEscape(),
                                          (ItemID != UUID.Zero) ? 1 : 0,
                                          ItemID,
                                          AssetType,
                                          ItemName.MySqlEscape()
                                      };

            data.Insert("osgroupnotice", Keys.ToArray(), Values.ToArray());
        }

        public void AddGroupProposal(UUID agentID, GroupProposalInfo info)
        {
            GenericUtils.AddGeneric(agentID, "Proposal", info.GroupID.ToString(), info.ToOSD(), data);
        }

        #endregion

        public void Dispose()
        {
        }

        public bool CheckGroupPermissions(UUID AgentID, UUID GroupID, ulong Permissions)
        {
            if (GroupID == UUID.Zero)
                return false;

            if (AgentID == UUID.Zero)
                return false;

            GroupMembersData GMD = GetAgentGroupMemberData(AgentID, GroupID, AgentID);
            GroupRecord record = GetGroupRecord(AgentID, GroupID, null);
            if (Permissions == 0)
            {
                if (GMD != null || record.FounderID == AgentID)
                    return true;
                return false;
            }

            if (record != null && record.FounderID == AgentID)
                return true;

            if (GMD == null)
                return false;

            if ((GMD.AgentPowers & Permissions) != Permissions)
                return false;

            return true;
        }
    }
}