namespace Elders.Pandora
{
    public class ApplicationContext : IPandoraContext
    {
        public ApplicationContext(string applicationName = null, string cluster = null, string machine = null)
        {
            this.ApplicationName = applicationName ?? EnvVar.GetApplication();
            this.Cluster = cluster ?? EnvVar.GetCluster();
            this.Machine = machine ?? EnvVar.GetMachine() ?? Box.Machine.NotSpecified;
        }

        public string ApplicationName { get; private set; }

        public string Cluster { get; private set; }

        public string Machine { get; private set; }

        public override string ToString()
        {
            return $"Pandora context: Cluster: {Cluster} | Machine: {Machine} | Application: {ApplicationName}";
        }
    }
}
