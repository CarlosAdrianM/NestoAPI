using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using NestoAPI.Infraestructure.Agencias.Gls;
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

        /// <summary>
        /// Ids de las agencias con TRAMITACIÓN remota server-side (las que <see cref="Crear"/> construye).
        /// </summary>
        IReadOnlyCollection<int> AgenciasConGestionRemota { get; }

        /// <summary>
        /// Estrategia de SEGUIMIENTO (solo lectura) de la agencia, o null si no tiene. La cumplen tanto
        /// las de tramitación (Innovatrans) como las que solo siguen (GLS). La usa el poll de estados.
        /// </summary>
        ISeguimientoAgenciaRemota CrearSeguimiento(int agenciaId);

        /// <summary>
        /// Ids de las agencias con SEGUIMIENTO (las que <see cref="CrearSeguimiento"/> construye). El poll
        /// filtra por aquí para no recorrer agencias sin integración. Añadir una = una línea aquí y en
        /// <see cref="CrearSeguimiento"/>.
        /// </summary>
        IReadOnlyCollection<int> AgenciasConSeguimiento { get; }
    }

    /// <summary>
    /// Factory de agencias con gestión remota. Hoy solo Innovatrans (Numero=12); el resto devuelve
    /// null (sin integración server-side). Compone la cadena DataTrans (config + cliente SOAP +
    /// operaciones de envío) y fija el remitente (nuestro almacén de Algete) desde Web.config. El
    /// identificador (nº de cliente DataTrans) se lee de AgenciaTransporte.Identificador (fila 12).
    /// </summary>
    public class FabricaAgenciasRemotas : IFabricaAgenciasRemotas
    {
        private readonly NVEntities _db;

        public FabricaAgenciasRemotas(NVEntities db)
        {
            _db = db;
        }

        // Tramitación server-side: solo Innovatrans. Seguimiento: Innovatrans + GLS (esta última solo
        // sigue, no tramita). Añadir una agencia nueva = una línea aquí y en Crear/CrearSeguimiento.
        private static readonly int[] _conGestionRemota = { Constantes.Agencias.AGENCIA_INNOVATRANS };
        private static readonly int[] _conSeguimiento = { Constantes.Agencias.AGENCIA_INNOVATRANS, Constantes.Agencias.AGENCIA_GLS };

        public IReadOnlyCollection<int> AgenciasConGestionRemota => _conGestionRemota;

        public IReadOnlyCollection<int> AgenciasConSeguimiento => _conSeguimiento;

        public ISeguimientoAgenciaRemota CrearSeguimiento(int agenciaId)
        {
            // Innovatrans: su estrategia de tramitación ya cumple el seguimiento (IAgenciaRemota lo hereda).
            if (agenciaId == Constantes.Agencias.AGENCIA_INNOVATRANS)
            {
                return Crear(agenciaId);
            }
            // GLS: solo seguimiento, vía su web de tracking (GetExpCli). uid de nuestra cuenta en Web.config.
            if (agenciaId == Constantes.Agencias.AGENCIA_GLS)
            {
                string uid = ConfigurationManager.AppSettings["GLS:UidSeguimiento"];
                return new SeguimientoAgenciaRemotaConReintentos(new AgenciaRemotaGls(new ClienteTrackingGls(uid)));
            }
            return null;
        }

        public IAgenciaRemota Crear(int agenciaId)
        {
            if (agenciaId != Constantes.Agencias.AGENCIA_INNOVATRANS)
            {
                return null;
            }

            // El identificador (nº de cliente DataTrans) vive en la tabla, como en las demás agencias.
            string identificador = _db.AgenciasTransportes
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
