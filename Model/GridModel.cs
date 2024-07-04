namespace SimBackend.Model
{
    public class GridModel
    {
        public List<Node> Nodes { get; set; }
        public List<Edge> Edges { get; set; }
        public List<Connection> Connections { get; set; }
    }

    public class Node
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Edge
    {
        public int Id { get; set; }
        public int Cost { get; set; }
        public TimeSpan Time { get; set; }
    }

    public class Connection
    {
        public int Id { get; set; }
        public int EdgeId { get; set; }
        public int FirstNodeId { get; set; }
        public int SecondNodeId { get; set; }
    }
}
