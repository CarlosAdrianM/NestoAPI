using System.Configuration;
using System.Linq;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Perfiles
{
    // NestoAPI#258 — los perfiles de las agencias que HOY integramos: cada clase declara su AgenciaId,
    // sus capacidades (las sub-interfaces IPerfilCon...) y CÓMO se compone cada una (Fase 2). El
    // registro los descubre por reflexión y la fábrica delega aquí. Al añadir una agencia basta con
    // crear su clase en este fichero (y que la puerta de activas la deje pasar).

    /// <summary>Innovatrans (DataTrans DTX): tramitación remota + seguimiento server-side.</summary>
    public class PerfilAgenciaInnovatrans : IPerfilConGestionRemota, IPerfilConSeguimiento
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_INNOVATRANS;

        public IAgenciaRemota CrearGestionRemota(NVEntities db)
        {
            // El identificador (nº de cliente DataTrans) vive en la tabla, como en las demás agencias.
            string identificador = db.AgenciasTransportes
                .Where(a => a.Numero == Constantes.Agencias.AGENCIA_INNOVATRANS)
                .Select(a => a.Identificador)
                .FirstOrDefault();

            // El registro de intercambios lo comparten el cliente (que lo escribe) y la estrategia
            // (que lo expone), para poder auditar el SOAP crudo de cada tramitación.
            var registro = new RegistroIntercambiosRemotos();
            var configuracion = new ConfiguracionInnovatrans(identificador?.Trim());
            var cliente = new ClienteSoapDataTrans(configuracion, registro: registro);
            var operaciones = new OperacionesEnviosDataTrans(cliente);
            var lectura = new OperacionesLecturaDataTrans(cliente);
            // Los transitorios de transporte (5xx, timeout, conexión) se reintentan aquí, en el
            // punto único (#288): consultar/reimprimir sí, insertar nunca (no es idempotente).
            return new AgenciaRemotaConReintentos(new AgenciaRemotaInnovatrans(operaciones, LeerRemitente(), registro, lectura));
        }

        // La estrategia de tramitación ya cumple el seguimiento (IAgenciaRemota lo hereda).
        public ISeguimientoAgenciaRemota CrearSeguimiento(NVEntities db) => CrearGestionRemota(db);

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

    /// <summary>GLS/ASM: solo SEGUIMIENTO (no tramita server-side) y con defaults de envío propios.</summary>
    public class PerfilAgenciaGls : IPerfilConSeguimiento, IPerfilConDefaultsEnvio
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_GLS;

        // Seguimiento vía su web de tracking (GetExpCli). uid de nuestra cuenta en Web.config.
        public ISeguimientoAgenciaRemota CrearSeguimiento(NVEntities db)
        {
            string uid = ConfigurationManager.AppSettings["GLS:UidSeguimiento"];
            return new SeguimientoAgenciaRemotaConReintentos(new AgenciaRemotaGls(new ClienteTrackingGls(uid)));
        }
    }

    /// <summary>Canteras (Canarias, operativa manual): tiene reglas propias de compatibilidad con el destino.</summary>
    public class PerfilAgenciaCanteras : IPerfilConReglasDestino
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_CANTERAS;
    }
}
