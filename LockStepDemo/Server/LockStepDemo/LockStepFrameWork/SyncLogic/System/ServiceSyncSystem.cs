﻿using DeJson;
using HDJ.Framework.Utils;
using LockStepDemo.GameLogic.Component;
using LockStepDemo.Service;
using LockStepDemo.Service.ServiceLogic.Component;
using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class ServiceSyncSystem : ServiceSystem
{
    public override void Init()
    {
        Debug.Log("ServiceSyncSystem init");

        AddEntityCreaterLisnter();
        AddEntityDestroyLisnter();

        //AddEntityCompAddLisenter();
        //AddEntityCompRemoveLisenter();

        m_world.eventSystem.AddListener(ServiceEventDefine.c_playerJoin, OnPlayerJoin);
        m_world.eventSystem.AddListener(ServiceEventDefine.c_playerExit, OnPlayerExit);

        //m_world.eventSystem.AddListener(ServiceEventDefine.c_ComponentChange, OnCompChange);
    }

    public override Type[] GetFilter()
    {
        return new Type[] {
                typeof(SyncComponent)
            };
    }

    public override void EndFrame(int deltaTime)
    {
        //全推送
        PushAllData();

        //推送开始消息
        PushStartSyncMsg();
    }

    public void SetAllSync(SyncComponent syncComp)
    {
        List<EntityBase> list = GetEntityList(new string[] { "ConnectionComponent" });

        for (int i = 0; i < list.Count; i++)
        {
            ConnectionComponent comp = list[i].GetComp<ConnectionComponent>();
            if (!syncComp.m_waitSyncList.Contains(comp))
            {
                syncComp.m_waitSyncList.Add(comp);
            }
        }
    }

    /// <summary>
    /// 把所有人同步给自己
    /// </summary>
    /// <param name="connectionComp"></param>
    public void SetAllSync(ConnectionComponent connectionComp)
    {
        List<EntityBase> list = GetEntityList();

        Debug.Log("OnPlayerJoin " + list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            //在所有同步对象的同步队列里加入这个新玩家 (把这个世界的数据告诉新玩家)
            SyncComponent sycTmp = list[i].GetComp<SyncComponent>();
            if (!sycTmp.m_waitSyncList.Contains(connectionComp))
            {
                sycTmp.m_waitSyncList.Add(connectionComp);
            }
        }
    }

    #region 事件接收

    public override void OnEntityCreate(EntityBase entity)
    {
        //Debug.Log("OnEntityCreate ID: " + entity.ID + " frame " + m_world.FrameCount);

        SyncComponent sc = null;
        //自动创建Sync组件
        if (!entity.GetExistComp<SyncComponent>())
        {
            //Debug.Log("自动创建Sync组件 ");
            sc = entity.AddComp<SyncComponent>();
        }
        else
        {
            sc = entity.GetComp<SyncComponent>();
        }

    }

    public override void OnEntityDestroy(EntityBase entity)
    {
        //Debug.Log("OnEntityDestroy ID: " + entity.ID + " frame " + m_world.FrameCount);

    }


    void OnPlayerJoin(EntityBase entity,params object[] objs)
    {
        //Debug.Log("ServiceSyncSystem OnPlayerJoin ");

        ConnectionComponent comp = entity.GetComp<ConnectionComponent>();
        SyncComponent syc = entity.GetComp<SyncComponent>();

        comp.m_isWaitPushStart = true;

        ////把自己广播给所有人
        //SetAllSync(syc);
    }

    void OnPlayerExit(EntityBase entity, params object[] objs)
    {
        ConnectionComponent comp = entity.GetComp<ConnectionComponent>();
        comp.m_isWaitPushStart = false;

        SyncComponent sc = entity.GetComp<SyncComponent>();
        SetAllSync(sc);

        //TODO 将来改成推送移除连接组件
        //PushDestroyEntity(sc, entity);
    }

    //void OnCompChange(EntityBase entity, params object[] objs)
    //{
    //    SyncComponent syc = entity.GetComp<SyncComponent>();
    //    SetAllSync(syc);
    //}

    #endregion

    #region 推送数据

    #region 游戏进程

    void PushStartSyncMsg()
    {
        List<EntityBase> list = GetEntityList(new string[] { "ConnectionComponent" });
        for (int i = 0; i < list.Count; i++)
        {
            ConnectionComponent comp = list[i].GetComp<ConnectionComponent>();

            if (comp.m_isWaitPushStart == true)
            {
                comp.m_isWaitPushStart = false;

                //同步单例组件
                foreach (var item in m_world.m_singleCompDict)
                {
                    if(item.Key != "EntityRecordComponent")
                    {
                        PushSingletonComp(comp.m_session, item.Key);
                    }
                }

                PushStartSyncMsg(comp.m_session);
            }
        }
    }

    void PushStartSyncMsg(SyncSession session)
    {
        Debug.Log("PushStartSyncMsg " + m_world.FrameCount);

        StartSyncMsg msg = new StartSyncMsg();
        msg.frame = m_world.FrameCount;
        msg.advanceCount = 1; //客户端提前一帧
        msg.intervalTime = UpdateEngine.IntervalTime;
        msg.createEntityIndex = 0;
        msg.randomSeed = m_world.m_RandomSeed;

        ProtocolAnalysisService.SendMsg(session, msg);
    }

    //推送所有数据(把所有同步队列的数据推送出去)
    public void PushAllData()
    {
        List<EntityBase> list = GetEntityList();
        List<EntityBase> playerList = GetEntityList(new string[] { "ConnectionComponent" });

        for (int i = 0; i < playerList.Count; i++)
        {
            ConnectionComponent cc = playerList[i].GetComp<ConnectionComponent>();
            if(cc.m_isWaitPushStart)
            {
                SetAllSync(cc);
            }
        }

        for (int i = 0; i < list.Count; i++)
        {
            SyncComponent sc = list[i].GetComp<SyncComponent>();
            PushSyncEnity(sc, list[i]);
        }

        for (int i = 0; i < playerList.Count; i++)
        {
            ConnectionComponent cc = playerList[i].GetComp<ConnectionComponent>();
            PushSyncEnity(cc, playerList[i]);
        }
    }

    #endregion

    #region 实体

    public void PushSyncEnity(SyncComponent syncComp, EntityBase entity)
    {
        for (int i = 0; i < syncComp.m_waitSyncList.Count; i++)
        {
            if(syncComp.m_waitSyncList[i].m_session != null)
            {
                syncComp.m_waitSyncList[i].m_waitSyncEntity.Add(entity);
            }
        }
        syncComp.m_waitSyncList.Clear();
    }

    public void PushSyncEnity(ConnectionComponent connect, EntityBase entity)
    {
        if (connect.m_session != null 
            && !connect.m_session.Connected)
        {
            return;
        }

        SyncEntityMsg msg = new SyncEntityMsg();
        msg.frame = m_world.FrameCount;

        msg.infos = new List<EntityInfo>();
        msg.destroyList = new List<int>();

        for (int i = 0; i < connect.m_waitSyncEntity.Count; i++)
        {
            msg.infos.Add(CreateEntityInfo(connect.m_waitSyncEntity[i], connect.m_session));
        }

        for (int i = 0; i < connect.m_waitDestroyEntity.Count; i++)
        {
            msg.destroyList.Add(connect.m_waitDestroyEntity[i]);
        }

        connect.m_waitDestroyEntity.Clear();
        connect.m_waitSyncEntity.Clear();
        if(msg.infos.Count > 0 || msg.destroyList.Count > 0)
        {

            List<EntityBase> list = GetEntityList();
            Debug.Log(" msg.infos " + msg.infos.Count + " world " + m_world.m_entityList.Count + " list " + list.Count);


            ProtocolAnalysisService.SendMsg(connect.m_session, msg);
        }
    }

    public EntityInfo CreateEntityInfo(EntityBase entity,SyncSession session)
    {
        EntityInfo Data = new EntityInfo();
        Data.id = entity.ID;
        Data.infos = new List<ComponentInfo>();

        foreach (var c in entity.m_compDict)
        {
            Type type = c.Value.GetType();

            if (!type.IsSubclassOf(typeof(ServiceComponent)))
            {
                try
                {
                    ComponentInfo info = new ComponentInfo();
                    info.m_compName = type.Name;
                    info.content = Serializer.Serialize(c.Value);

                    Data.infos.Add(info);
                }
                catch(StackOverflowException e)
                {
                    Debug.LogError("Serializer error " + type.FullName + " " + e.ToString());
                }
            }
        }

        //给有连接组件的增加Self组件
        if (entity.GetExistComp<ConnectionComponent>())
        {
            ConnectionComponent comp = entity.GetComp<ConnectionComponent>();
            if (comp.m_session == session)
            {
                ComponentInfo info = new ComponentInfo();
                info.m_compName = "SelfComponent";
                info.content = "{}";
                Data.infos.Add(info);
            }
            else
            {
                ComponentInfo info = new ComponentInfo();
                info.m_compName = "TheirComponent";
                info.content = "{}";
                Data.infos.Add(info);
            }
        }

        return Data;
    }


    public void PushDestroyEntity(SyncComponent connectionComp, EntityBase entity)
    {
        for (int i = 0; i < connectionComp.m_waitSyncList.Count; i++)
        {
            connectionComp.m_waitSyncList[i].m_waitDestroyEntity.Add(entity.ID);
        }
        connectionComp.m_waitSyncList.Clear();
    }

    //void PushDestroyEntity(SyncSession session, int entityID)
    //{
    //    DestroyEntityMsg msg = new DestroyEntityMsg();
    //    msg.id = entityID;
    //    msg.frame = m_world.FrameCount;

    //    ProtocolAnalysisService.SendMsg(session, msg);

    //    Debug.Log("PushDestroyEntity 3");
    //}

    #endregion

    #region 单例组件

    public void PushSingletonComp<T>(SyncSession session) where T : SingletonComponent, new()
    {
        string key = typeof(T).Name;
        PushSingletonComp(session, key);
    }

    public void PushSingletonComp(SyncSession session, string compName)
    {
        Debug.Log("PushSingletonComp " + compName);

        SingletonComponent comp = m_world.GetSingletonComp(compName);
        ChangeSingletonComponentMsg msg = new ChangeSingletonComponentMsg();

        msg.info = new ComponentInfo();
        msg.info.m_compName = compName;
        msg.info.content = Serializer.Serialize(comp);
        msg.frame = m_world.FrameCount;

        ProtocolAnalysisService.SendMsg(session, msg);
    }

    #endregion

    #endregion
}

