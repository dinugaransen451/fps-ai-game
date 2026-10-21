using System.Collections.Generic;

namespace ZombieGame
{
    public enum BTStatus { Success, Failure, Running }

    /// <summary>Minimal behavior tree (used by the Tank). Reactive: the tree is re-evaluated from the root every tick.</summary>
    public abstract class BTNode
    {
        public string Name;
        public BTStatus LastStatus;
        public abstract BTStatus Tick();
        public virtual void Collect(List<BTNode> activePath) { }
    }

    /// <summary>Tries children in priority order; returns the first that does not fail.</summary>
    public class BTSelector : BTNode
    {
        readonly List<BTNode> children = new List<BTNode>();
        BTNode active;

        public BTSelector(string name, params BTNode[] nodes)
        {
            Name = name;
            children.AddRange(nodes);
        }

        public override BTStatus Tick()
        {
            active = null;
            for (int i = 0; i < children.Count; i++)
            {
                BTStatus s = children[i].Tick();
                children[i].LastStatus = s;
                if (s != BTStatus.Failure)
                {
                    active = children[i];
                    LastStatus = s;
                    return s;
                }
            }
            LastStatus = BTStatus.Failure;
            return BTStatus.Failure;
        }

        public override void Collect(List<BTNode> path)
        {
            path.Add(this);
            if (active != null) active.Collect(path);
        }
    }

    /// <summary>Runs children in order; fails as soon as one fails.</summary>
    public class BTSequence : BTNode
    {
        readonly List<BTNode> children = new List<BTNode>();
        BTNode last;

        public BTSequence(string name, params BTNode[] nodes)
        {
            Name = name;
            children.AddRange(nodes);
        }

        public override BTStatus Tick()
        {
            last = null;
            for (int i = 0; i < children.Count; i++)
            {
                last = children[i];
                BTStatus s = children[i].Tick();
                children[i].LastStatus = s;
                if (s != BTStatus.Success)
                {
                    LastStatus = s;
                    return s;
                }
            }
            LastStatus = BTStatus.Success;
            return BTStatus.Success;
        }

        public override void Collect(List<BTNode> path)
        {
            path.Add(this);
            if (last != null) last.Collect(path);
        }
    }

    public class BTCondition : BTNode
    {
        readonly System.Func<bool> test;

        public BTCondition(string name, System.Func<bool> test)
        {
            Name = name;
            this.test = test;
        }

        public override BTStatus Tick() { return test() ? BTStatus.Success : BTStatus.Failure; }
        public override void Collect(List<BTNode> path) { path.Add(this); }
    }

    public class BTAction : BTNode
    {
        readonly System.Func<BTStatus> act;

        public BTAction(string name, System.Func<BTStatus> act)
        {
            Name = name;
            this.act = act;
        }

        public override BTStatus Tick() { return act(); }
        public override void Collect(List<BTNode> path) { path.Add(this); }
    }

    public class BehaviorTree
    {
        public readonly BTNode Root;
        public readonly List<BTNode> ActivePath = new List<BTNode>();

        public BehaviorTree(BTNode root) { Root = root; }

        public BTStatus Tick()
        {
            BTStatus s = Root.Tick();
            ActivePath.Clear();
            Root.Collect(ActivePath);
            return s;
        }

        /// <summary>e.g. "Root > Chase > MoveToPlayer" - shown in the debug HUD.</summary>
        public string ActivePathString()
        {
            string s = "";
            for (int i = 0; i < ActivePath.Count; i++)
            {
                if (i > 0) s += " > ";
                s += ActivePath[i].Name;
            }
            return s;
        }
    }
}
