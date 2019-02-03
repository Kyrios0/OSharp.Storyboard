﻿using OSharp.Storyboard.Events;
using OSharp.Storyboard.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OSharp.Storyboard.Management
{
    public static class ElementCompress
    {
        public static void Compress(this Element element)
        {
            element.Examine();
            element.FillObsoleteList();
            // 每个类型压缩从后往前
            // 1.删除没用的
            // 2.整合能整合的
            // 3.考虑单event情况
            // 4.排除第一行误加的情况（defaultParams）
            PreOptimize(element);
            NormalOptimize(element);
        }

        /// <summary>
        /// 预压缩
        /// </summary>
        private static void PreOptimize(EventContainer container)
        {
            if (container is Element ele)
            {
                foreach (var item in ele.LoopList)
                {
                    PreOptimize(item);
                }

                foreach (var item in ele.TriggerList)
                {
                    PreOptimize(item);
                }
            }

            if (container.EventList.Any())
                RemoveByObsoletedList(container, container.EventList.ToList());
        }

        /// <summary>
        /// 根据ObsoletedList，移除不必要的命令。
        /// </summary>
        private static void RemoveByObsoletedList(EventContainer container, List<Event> eventList)
        {
            if (container.ObsoleteList.TimingList.Count == 0) return;
            var groups = eventList.GroupBy(k => k.EventType).Where(k => k.Key != EventType.Fade);
            foreach (var group in groups)
            {
                var list = group.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    Event nowE = list[i];
                    Event nextE =
                        i == list.Count - 1
                            ? null
                            : list[i + 1];

                    /*
                     * 若当前Event在某Obsolete Range内，且下一Event的StartTime也在此Obsolete Range内，则删除。
                     * 若当前Event是此种类最后一个（无下一个Event），那么需要此Event在某Obsolete Range内，且此Obsolete Range持续到Container结束。
                     * 另注意：若此Event为控制Obsolete Range的Event，则将其过滤。（判断是否正好在某段Obsolete Range的StartTime或EndTime上）
                    */

                    // 判断是否此Event为控制Obsolete Range的Event。
                    if (!(nowE.OnObsoleteTimingRange(container) &&
                          EventExtension.UnworthyDictionary.ContainsKey(nowE.EventType)))
                    {
                        bool canRemove;

                        // 若当前Event是此种类最后一个（无下一个Event)。
                        if (nextE == null)
                        {
                            // 判断是否此Event在某Obsolete Range内，且此Obsolete Range持续到Container结束。
                            canRemove = nowE.InObsoleteTimingRange(container, out var range) &&
                                        range.EndTime == container.MaxTime;
                        }
                        else
                        {
                            // 判断是否此Event在某Obsolete Range内，且下一Event的StartTime也在此Obsolete Range内。
                            canRemove = container.ObsoleteList.ContainsTimingPoint(out _,
                                nowE.StartTime, nowE.EndTime, nextE.StartTime);
                        }

                        if (canRemove)
                        {
                            RemoveEvent(container, list, nowE);
                            i--;
                        }
                    }

                    // 判断当前种类最后一个动作是否正处于物件透明状态，而且此状态最大时间即是obj最大时间
                }
            }
        }

        /// <summary>
        /// 正常压缩
        /// </summary>
        private static void NormalOptimize(EventContainer container)
        {
            if (container is Element ele)
            {
                foreach (var item in ele.LoopList)
                {
                    RemoveByLogic(item, container.EventList.ToList());
                }

                foreach (var item in ele.TriggerList)
                {
                    RemoveByLogic(item, container.EventList.ToList());
                }
            }

            if (container.EventList.Any())
            {
                RemoveByLogic(container, container.EventList.ToList());
            }
        }

        /// <summary>
        /// 根据逻辑，进行命令优化。
        /// </summary>
        /// <param name="container"></param>
        /// <param name="eventList"></param>
        private static void RemoveByLogic(EventContainer container, List<Event> eventList)
        {
            var groups = eventList.GroupBy(k => k.EventType);
            foreach (var group in groups)
            {
                EventType type = group.Key;
                var list = group.ToList();

                int index = list.Count - 1;
                while (index >= 0)
                {
                    Event nowE = list[index];

                    // 首个event     
                    if (index == 0)
                    {
                        //S,0,300,,1
                        //S,0,400,500,0.5
                        /* 
                         * 当 此event结束时间 < obj最大时间 (或包括此event有两个以上的最大时间)
                         * 且 此event开始时间 > obj最小时间 (或包括此event有两个以上的最小时间)
                         * 且 此event的param固定
                         * 且 此event.param=default
                         * 且 唯一
                         */
                        if (nowE.IsTimeInRange(container) && nowE.IsStaticAndDefault() &&
                            list.Count == 1)
                        {
                            // Remove
                            RemoveEvent(container, list, nowE);
                        }
                        /*
                         * 当 此event为move，param固定，且唯一时
                         */
                        else if (type == EventType.Move
                                 && container is Element element)
                        {
                            if (list.Count == 1 && nowE.IsStatic())
                            {
                                var move = (Move)nowE;
                                if (nowE.Start.All(k => k == (int)k)) //若为小数，不归并
                                {
                                    element.DefaultX = move.StartX;
                                    element.DefaultY = move.StartY;

                                    // Remove
                                    RemoveEvent(container, list, nowE);
                                }
                                else if (move.EqualsInitialPosition(element))
                                {
                                    // Remove
                                    RemoveEvent(container, list, nowE);
                                }
                                else
                                {
                                    element.DefaultX = 0;
                                    element.DefaultY = 0;
                                }
                            }
                            else
                            {
                                element.DefaultX = 0;
                                element.DefaultY = 0;
                            }
                        }
                        break;
                    }
                    else
                    {
                        Event preE = list[index - 1];
                        // 优先进行合并，若不符合再进行删除。
                        /*
                         * 当 此event与前event一致，且前后param皆固定
                        */
                        if (nowE.IsStatic()
                            && preE.IsStatic()
                            && EventCompare.IsEventSequent(preE, nowE))
                        {
                            preE.EndTime = nowE.EndTime;  // 整合至前面: 前一个命令的结束时间延伸

                            //if (preStartT == container.MinTime && container.MinTimeCount > 1) // todo: optimize: ?
                            //{
                            //    //preE.StartTime = preE.EndTime; // old
                            //    preE.EndTime = preE.StartTime;
                            //}

                            // Remove
                            RemoveEvent(container, list, nowE);
                            //index = list.Count - 1; // todo: optimize: ?
                            index--;
                        }
                        /*
                         * 当 此event结束时间 < obj最大时间 (或包括此event有两个以上的最大时间)
                         * 且 此event的param固定
                         * 且 此event当前动作 = 此event上个动作
                         * (包含一个F的特例) todo: optimize: ?
                        */
                        else if (nowE.IsSmallerThenMaxTime(container) /*||
                                 type == EventType.Fade && nowStartP.SequenceEqual(EventExtension.UnworthyDictionary[EventType.Fade]) */
                                 && nowE.IsStatic()
                                 && EventCompare.IsEventSequent(preE, nowE))
                        {
                            // Remove
                            RemoveEvent(container, list, nowE);
                            //index = list.Count - 1; // todo: optimize: ?
                            index--;
                        }
                        // 存在一种非正常的无效情况，例如：
                        // F,0,0,,0
                        // F,0,0,5000,1
                        // S,0,0,,0.5,0.8
                        // 此时，第一行的F可被删除。或者：
                        // F,0,0,,1
                        // F,0,1000,,0
                        // F,0,1000,5000,1
                        // S,0,0,,0.5,0.8
                        // 此时，第二行的F可被删除。
                        else if (nowE.StartTime == preE.EndTime &&
                                 preE.StartTime == preE.EndTime)
                        {
                            if (index > 1)
                            {
                                // Remove
                                RemoveEvent(container, list, preE);
                                index--;
                            }
                            else if (preE.EqualsMultiMinTime(container))
                            {
                                // Remove
                                RemoveEvent(container, list, preE);
                                index--;
                            }
                            else if (preE.IsStatic() && EventCompare.IsEventSequent(preE, nowE))
                            {
                                // Remove
                                RemoveEvent(container, list, preE);
                                index--;
                            }
                            else
                                index--;
                        }
                        else index--;
                    }
                }
            }
        }

        private static void RemoveEvent(EventContainer sourceContainer, ICollection<Event> eventList, Event e)
        {
            sourceContainer.EventList.Remove(e);
            eventList.Remove(e);
        }
    }
}