using System.Configuration;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    public interface IFabricaAgenciasRemotas
    {
        /// <summary>
        /// Devuelve la estrategia de gestión remota de la agencia, o <c>null</c> si esa agencia no
        /// tiene integración (Canteras, GLS de momento, etc.): el llamante hace solo el flujo de BD.
        /// </summary>
        IAgenciaRemota Crear(int agenciaId);
    }

    /// <summary>
    /// Factory de agencias con gestión remota. Hoy solo Innovatrans (Numero=12); el resto devuelve
    /// null (sin integración server-side). Compone la cadena DataTrans (config + cliente SOAP +
    /// operaciones de envío) y fija el remitente (nuestro almacén de Algete) desde Web.config.
    /// </summary>
    public class FabricaAgenciasRemotas : IFabricaAgenciasRemotas
    {
        public IAgenciaRemota Crear(int agenciaId)
        {
            if (agenciaId != Constantes.Agencias.AGENCIA_INNOVATRANS)
            {
                return null;
            }

            var configuracion = new ConfiguracionInnovatrans();
            var cliente = new ClienteSoapDataTrans(configuracion);
            var operaciones = new OperacionesEnviosDataTrans(cliente);
            return new AgenciaRemotaInnovatrans(operaciones, LeerRemitente());
        }

        // Remitente fijo (nuestro almacén de Algete). Se configura en Web.config para no hardcodearlo.
        private static DireccionDataTrans LeerRemitente()
        {
            return new DireccionDataTrans
            {
                Pais = MapeadorDireccionDataTrans.PAIS_ESPANA,
                Nombre = ConfigurationManager.AppSettings["Innovatrans:Remitente:Nombre"],
                Telefono = ConfigurationManager.AppSettings["Innovatrans:Remitente:Telefono"],
                CodigoPostal = ConfigurationManager.AppSettings["Innovatrans:Remitente:CodigoPostal"],
                Poblacion = ConfigurationManager.AppSettings["Innovatrans:Remitente:Poblacion"],
                Direccion = ConfigurationManager.AppSettings["Innovatrans:Remitente:Direccion"]
            };
        }
    }
}
