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

        public (short Servicio, short Horario, int Pais) DefaultsEnvio(string codPostal)
            => (Servicio: 96, Horario: 18, Pais: 34); // BusinessParcel, Economy, España
    }

    /// <summary>Canteras (Canarias, operativa manual): tiene reglas propias de compatibilidad con el destino.</summary>
    public class PerfilAgenciaCanteras : IPerfilConReglasDestino
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_CANTERAS;

        // NestoAPI#204: Canteras solo opera en Canarias, sin reembolso y con mínimo de importe.
        public string ValidarDestino(string codPostal, CabPedidoVta pedido, bool cobrarReembolso)
        {
            if (!PedidosVenta.GestorPortes.EsCanarias(codPostal))
            {
                return "La agencia Canteras solo opera en Canarias (códigos postales 35xxx y 38xxx).";
            }
            if (cobrarReembolso)
            {
                return "La agencia Canteras no admite contra reembolso. Cobra el pedido por otro medio antes de tramitar el envío.";
            }

            decimal baseImponiblePedido = pedido.LinPedidoVtas?
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                .Sum(l => l.Base_Imponible) ?? 0M;
            bool llevaLineaPortesCanarias = pedido.LinPedidoVtas?
                .Any(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE
                    && l.Producto != null
                    && l.Producto.Trim().StartsWith("624")
                    && l.Base_Imponible >= Constantes.Portes.CANARIAS) ?? false;

            if (baseImponiblePedido < Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS
                && !llevaLineaPortesCanarias)
            {
                return $"El pedido no llega al mínimo de Canarias ({Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS:N0} €) " +
                       $"y no lleva una línea de portes de {Constantes.Portes.CANARIAS:N0} €. Añade portes o aumenta el importe.";
            }
            return null;
        }
    }

    /// <summary>
    /// Correos Express: en cuarentena (no se tramita), pero sus reglas de destino y defaults siguen
    /// aplicando a las etiquetas que se creen a mano.
    /// </summary>
    public class PerfilAgenciaCorreosExpress : IPerfilConReglasDestino, IPerfilConDefaultsEnvio
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_CORREOS_EXPRESS;

        public string ValidarDestino(string codPostal, CabPedidoVta pedido, bool cobrarReembolso)
            => PedidosVenta.GestorPortes.EsCanarias(codPostal)
                ? "Correos Express no entrega en Canarias. Usa la agencia Canteras para envíos a Canarias."
                : null;

        public (short Servicio, short Horario, int Pais) DefaultsEnvio(string codPostal)
        {
            if (EsCodigoPostalPortugues(codPostal))
            {
                return (Servicio: 63, Horario: 0, Pais: 724); // Paq24, Portugal
            }
            if (EsCodigoPostalEspanol(codPostal))
            {
                return (Servicio: 93, Horario: 0, Pais: 724); // ePaq24, España
            }
            return (Servicio: 90, Horario: 0, Pais: 724); // Internacional monobulto
        }

        internal static bool EsCodigoPostalEspanol(string codPostal)
        {
            return codPostal.Length == 5 && int.TryParse(codPostal, out int cp) && cp >= 1000 && cp <= 52999;
        }

        internal static bool EsCodigoPostalPortugues(string codPostal)
        {
            // Formato portugués: 4 dígitos o 4 dígitos-3 dígitos (ej: "1000" o "1000-001")
            string sinGuion = codPostal.Replace("-", "");
            return (codPostal.Length == 4 || codPostal.Length == 8)
                && int.TryParse(sinGuion, out int cp) && cp >= 1000 && cp <= 9999999;
        }
    }

    /// <summary>Sending: en cuarentena (no se tramita), pero conserva sus defaults de envío.</summary>
    public class PerfilAgenciaSending : IPerfilConDefaultsEnvio
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_SENDING;

        public (short Servicio, short Horario, int Pais) DefaultsEnvio(string codPostal)
            => (Servicio: 1, Horario: 1, Pais: 34); // Send Express, Normal, España
    }
}
