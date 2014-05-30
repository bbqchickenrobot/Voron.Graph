﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Voron.Graph.Algorithms.Search
{
    public enum TraversalType
    {
        BFS,
        DFS
    }

    public class SearchAlgorithm : BaseAlgorithm
    {
        private readonly TraversalType _traversalType;
        private readonly ITraversalStorage<TraversalNodeInfo> _processingQueue;
        private readonly HashSet<long> VisitedNodes;
        private readonly Transaction _tx;
        private readonly GraphStorage _graphStorage;
        private CancellationToken _cancelToken;
        private readonly Node _rootNode;

        public Func<JObject, bool> SearchPredicate { get; set; }

        public Func<IEnumerable<Node>, bool> ShouldStopSearch;

        public ushort? EdgeTypeFilter { get; set; }

        public uint? TraverseDepthLimit { get; set; }

        public IVisitor Visitor { get; set; }

        public SearchAlgorithm(Transaction tx, 
            GraphStorage graphStorage, 
            Node rootNode, 
            TraversalType traversalType,
            CancellationToken cancelToken)
        {
            _traversalType = traversalType;
            _processingQueue = (_traversalType == TraversalType.BFS) ?
                (ITraversalStorage<TraversalNodeInfo>)(new BfsTraversalStorage<TraversalNodeInfo>()) : new DfsTraversalStorage<TraversalNodeInfo>();
            
            _cancelToken = cancelToken;
            VisitedNodes = new HashSet<long>();
            _graphStorage = graphStorage;
            _tx = tx;
            _rootNode = rootNode;

            _processingQueue.Put(new TraversalNodeInfo
            {
                CurrentNode = rootNode,
                LastEdgeWeight = 0,
                ParentNode = null,
                TotalEdgeWeightUpToNow = 0,
                TraversalDepth = 1
            });
            VisitedNodes.Add(rootNode.Key);
        }       

        public IEnumerable<Node> Traverse()
        {
            if (State == AlgorithmState.Running)
                throw new InvalidOperationException("The algorithm is already running");

            OnStateChange(AlgorithmState.Running);

            if (Visitor != null && _rootNode != null) //precaution
                Visitor.DiscoverNode(_rootNode);

            var results = new List<Node>();
            while (_processingQueue.Count > 0)
            {
                _cancelToken.ThrowIfCancellationRequested();

                var traversalInfo = _processingQueue.GetNext();
                if (Visitor != null)
                    Visitor.ExamineTraversal(traversalInfo);

                if (SearchPredicate != null && SearchPredicate(traversalInfo.CurrentNode.Data))
                    results.Add(traversalInfo.CurrentNode);

                if(ShouldStopSearch != null && ShouldStopSearch(results))
                {
                    OnStateChange(AlgorithmState.Aborted);
                    break;
                }

                foreach (var childNodeWithEdge in
                    _graphStorage.Queries.GetAdjacentOf(_tx, traversalInfo.CurrentNode, EdgeTypeFilter ?? 0)
                                         .Where(nodeWithEdge => !VisitedNodes.Contains(nodeWithEdge.Node.Key)))
                {
                    _cancelToken.ThrowIfCancellationRequested();

                    VisitedNodes.Add(childNodeWithEdge.Node.Key);
                    if (Visitor != null)
                    {
                        Visitor.DiscoverEdge(childNodeWithEdge.EdgeTo);
                        Visitor.DiscoverNode(childNodeWithEdge.Node);
                    }

                    _processingQueue.Put(new TraversalNodeInfo
                    {
                        CurrentNode = childNodeWithEdge.Node,
                        LastEdgeWeight = childNodeWithEdge.EdgeTo.Key.Weight,
                        ParentNode = traversalInfo.CurrentNode,
                        TraversalDepth = traversalInfo.TraversalDepth + 1,
                        TotalEdgeWeightUpToNow = traversalInfo.TotalEdgeWeightUpToNow + childNodeWithEdge.EdgeTo.Key.Weight                        
                    });
                }
            }

            OnStateChange(AlgorithmState.Finished);
            return results;
        }

        public Task<IEnumerable<Node>> TraverseAsync()
        {
            return Task.Run(() => Traverse(), _cancelToken);
        }       

        #region Traversal Storage Implementations

        private interface ITraversalStorage<T> : IEnumerable<T>
        {
            T GetNext();
            void Put(T item);

            int Count { get; }
        }

        private class BfsTraversalStorage<T> : ITraversalStorage<T>
        {
            private readonly Queue<T> _traversalStorage;

            public BfsTraversalStorage()
            {
                _traversalStorage = new Queue<T>();
            }

            public T GetNext()
            {
                return _traversalStorage.Dequeue();
            }

            public void Put(T item)
            {
                _traversalStorage.Enqueue(item);
            }


            public int Count
            {
                get { return _traversalStorage.Count; }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }
        }

        private class DfsTraversalStorage<T> : ITraversalStorage<T>
        {
            private readonly Stack<T> _traversalStorage;

            public DfsTraversalStorage()
            {
                _traversalStorage = new Stack<T>();
            }

            public T GetNext()
            {
                return _traversalStorage.Pop();
            }

            public void Put(T item)
            {
                _traversalStorage.Push(item);
            }

            public int Count
            {
                get { return _traversalStorage.Count; }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return _traversalStorage.GetEnumerator();
            }
        }

        #endregion
    }
}