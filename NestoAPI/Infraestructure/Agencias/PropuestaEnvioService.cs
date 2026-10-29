using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PreparacionAlmacen;
using TextosConsultaReembolso = NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso.TextosConsultaReembolso;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 1): propone el envío de agencia de un pedido con las MISMAS reglas que Agencias de
    /// Nesto (Nesto.ViewModels/AgenciasViewModel.vb). Las líneas de VB citadas son las del 06/10/26.
    /// </summary>
    public interface IPropuestaEnvioService
    {
        /// <param name="bultos">Los que diga quien llama; si no, los del packing de Ariadna; si no, 1.</param>
        /// <param name="peso">El que diga quien llama; si no, 0.</param>
        /// <param name="excluirEnvio">Envío que no cuenta como ampliación (la sombra del POST excluye el que acaba de grabar).</param>
        /// <returns>null si el pedido no existe.</returns>
        Task<PropuestaEnvioDTO> Calcular(string empresa, int pedido, short? bultos, decimal? peso, int? excluirEnvio = null);
    }

    /// <summary>
    /// Lo que la propuesta lee de la base de datos, detrás de una interfaz para probar las reglas sin BD. La
    /// implementación real (<see cref="DatosPropuestaEnvioEF"/>) reutiliza los gestores que ya sirven a Nesto.
    /// </summary>
    public interface IDatosPropuestaEnvio
    {
        /// <summary>El pedido con la ficha del cliente/contacto (GET PedidosVenta/ParaAgencia).</summary>
        Task<PedidoParaAgenciaDTO> LeerPedido(string empresa, int pedido);
        /// <summary>GET PedidosVenta/ParaAgencia/SituacionLineas.</summary>
        Task<bool> TieneAlgunaLineaConPicking(string empresa, int pedido);
        /// <summary>GET EnviosAgencias/PendientePorPedido: Estado &lt; 0, el más antiguo.</summary>
        Task<EnviosAgencia> LeerEnvioPendiente(string empresa, int pedido);
        /// <summary>GET EnviosAgencias/EnCursoPorClienteYDireccion: Estado = 0, mismo cliente, contacto y dirección.</summary>
        Task<EnviosAgencia> LeerEnvioAmpliacion(string cliente, string contacto, string direccion, int? excluirEnvio);
        /// <summary>GET PedidosVenta/ImporteReembolso.</summary>
        decimal ImporteReembolso(string empresa, int pedido);
        /// <summary>GET PedidosVenta/PrepagosPendientes (#569).</summary>
        decimal PrepagosPendientes(string empresa, int pedido);
        /// <summary>NestoAPI#679: los enlaces de pago (TPV virtual) pendientes o autorizados del pedido.</summary>
        List<EnlacePagoPedido> EnlacesDePago(string empresa, int pedido);
        /// <summary>NestoAPI#686: el envío sale sin reembolso porque el pedido es al contado con «Mantener junto» y le quedan líneas pendientes.</summary>
        bool SaleSinReembolsoPorMantenerJunto(string empresa, int pedido);
        /// <summary>Bultos distintos del packing de Ariadna (GET Almacen/Pedidos/{pedido}/Bultos); 0 si no hay.</summary>
        Task<int> BultosPacking(string empresa, int pedido);
        Task<List<AgenciaTransporte>> LeerAgencias();
        Task<DateTime?> FechaPicking(string empresa);
        /// <summary>El comparador del servidor (GET Agencias/MasEconomica y /{numero}/Coste).</summary>
        ComparadorAgencias Comparador();
    }

    public class PropuestaEnvioService : IPropuestaEnvioService
    {
        // AgenciaASM/AgenciaGestionadaPorApi/AgenciaCanteras.vb: paisDefecto = 34 y retornoSinRetorno = 0 en todas.
        internal const int PAIS_ESPANA = 34;
        internal const int PAIS_PORTUGAL = 351;
        internal const string VENDEDOR_POR_DEFECTO = "NV";
        private const int LONGITUD_OBSERVACIONES = 80;
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        // Registro SIN puerta, como el catálogo: los defaults aplican también a agencias en cuarentena o sombra.
        private static readonly RegistroAgencias _perfiles = RegistroAgencias.PorReflexionSinPuerta();

        private readonly IDatosPropuestaEnvio datos;
        private readonly Func<string, int, Task<ConsultaReembolsoDTO>> leerConsultaReembolso;

        /// <summary>NestoAPI#739: el interruptor Agencias:RepartoPropio. Sustituible en los tests.</summary>
        internal Func<bool> RepartoPropioActivo { get; set; } = RepartoPropio.InterruptorRepartoPropio.EstaActivo;

        /// <param name="leerConsultaReembolso">NestoAPI#734: la última consulta de reembolso del pedido (null = no se mira).</param>
        public PropuestaEnvioService(IDatosPropuestaEnvio datos, Func<string, int, Task<ConsultaReembolsoDTO>> leerConsultaReembolso = null)
        {
            this.datos = datos;
            this.leerConsultaReembolso = leerConsultaReembolso;
        }

        public async Task<PropuestaEnvioDTO> Calcular(string empresa, int pedido, short? bultos, decimal? peso, int? excluirEnvio = null)
        {
            PedidoParaAgenciaDTO pedidoAgencia = await datos.LeerPedido(empresa, pedido).ConfigureAwait(false);
            if (pedidoAgencia == null)
            {
                return null;
            }

            // VB 493-494: la ficha del cliente/contacto del pedido y el reembolso que propone el servidor.
            ClienteParaAgenciaDTO ficha = pedidoAgencia.ClienteFicha ?? new ClienteParaAgenciaDTO();
            decimal reembolsoPedido = datos.ImporteReembolso(empresa, pedido);
            // VB 480-488 (#569): best-effort, si no se pueden leer no hay aviso.
            decimal prepagos = LeerPrepagos(empresa, pedido);
            // NestoAPI#679: los enlaces de pago del pedido (best-effort, como los prepagos). Los cobrados ya van en prepagos.
            List<EnlacePagoPedido> enlaces = LeerEnlacesDePago(empresa, pedido);
            // VB 498 + 573-576 (Nesto#507): 1 por defecto; los del packing de Ariadna si los hay. Lo que teclea el usuario manda.
            short bultosPropuestos = bultos.HasValue && bultos.Value > 0 ? bultos.Value : await BultosDelPacking(empresa, pedido).ConfigureAwait(false);
            decimal pesoPropuesto = peso ?? 0m;

            var propuesta = new PropuestaEnvioDTO
            {
                Pedido = pedido,
                Cliente = pedidoAgencia.Cliente?.Trim(),
                Contacto = pedidoAgencia.Contacto?.Trim(),
                Bultos = bultosPropuestos,
                Peso = pesoPropuesto,
                PrepagosPendientes = prepagos,
                EnlacesPagoAutorizados = EnlacesPagoPedido.ImporteAutorizado(enlaces),
                EnlacesPagoPendientes = EnlacesPagoPedido.PendientesVivos(enlaces, DateTime.Now),
                // NestoAPI#734: si se ha consultado a Administración, qué dijo (best-effort: sin ella, como antes).
                ConsultaReembolso = await LeerConsultaReembolso(empresa, pedido).ConfigureAwait(false)
            };

            List<AgenciaTransporte> agencias = await datos.LeerAgencias().ConfigureAwait(false) ?? new List<AgenciaTransporte>();

            // VB 520 / 3423: si el pedido tiene un envío PENDIENTE (etiqueta de la tienda online o de NestoApp), se reutiliza.
            EnviosAgencia pendiente = await datos.LeerEnvioPendiente(empresa, pedido).ConfigureAwait(false);
            if (pendiente != null)
            {
                RellenarDesdePendiente(propuesta, pendiente, empresa, reembolsoPedido);
            }
            else
            {
                // VB 3426 + 3794-3803: si no, ampliación de un envío en curso (Estado 0) al mismo cliente, contacto y dirección.
                string direccionFicha = ficha.Direccion?.Trim() ?? string.Empty;
                EnviosAgencia ampliacion = await datos.LeerEnvioAmpliacion(pedidoAgencia.Cliente, pedidoAgencia.Contacto,
                    direccionFicha, excluirEnvio).ConfigureAwait(false);
                await RellenarNuevoOAmpliacion(propuesta, pedidoAgencia, ficha, ampliacion, empresa, reembolsoPedido, agencias).ConfigureAwait(false);
            }

            // NestoAPI#734: Administración dijo «sin reembolso». Un envío pendiente (tienda online, NestoApp) trae su propio reembolso,
            // que no ha viajado a ninguna agencia: se propone sin él. Uno nuevo ya sale a 0 porque la forma de pago se cambió.
            if (propuesta.ConsultaReembolso?.ConReembolso == false && propuesta.Origen == PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO)
            {
                propuesta.Reembolso = 0m;
            }

            propuesta.AgenciaNombre = agencias.FirstOrDefault(a => a.Numero == propuesta.Agencia)?.Nombre?.Trim();
            await AnadirAvisos(propuesta, empresa, pedido).ConfigureAwait(false);
            return propuesta;
        }

        // ---------------------------------------------------------------- pendiente

        private void RellenarDesdePendiente(PropuestaEnvioDTO propuesta, EnviosAgencia pendiente, string empresa, decimal reembolsoPedido)
        {
            // VB 3428 + 3505-3517: InsertarRegistro graba el MISMO envío pendiente cambiando solo Estado, Bultos,
            // Servicio/Retorno, Peso e ImporteGasto. Todo lo demás (destino, reembolso, vendedor, fechas...) es el del
            // pendiente: ese destino manda (Nesto#395, VB 529-539).
            propuesta.Origen = PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO;
            propuesta.EnvioOrigen = pendiente.Numero;
            propuesta.Empresa = pendiente.Empresa?.Trim();
            // VB 541: la agencia es la del envío pendiente, no la del comparador.
            propuesta.Agencia = pendiente.Agencia;
            propuesta.Nombre = pendiente.Nombre?.Trim();
            propuesta.Direccion = pendiente.Direccion?.Trim();
            propuesta.CodPostal = pendiente.CodPostal?.Trim();
            propuesta.Poblacion = pendiente.Poblacion?.Trim();
            propuesta.Provincia = pendiente.Provincia?.Trim();
            propuesta.Telefono = pendiente.Telefono?.Trim();
            propuesta.Movil = pendiente.Movil?.Trim();
            propuesta.Email = pendiente.Email?.Trim();
            propuesta.Atencion = pendiente.Atencion?.Trim();
            propuesta.Observaciones = Recortar(pendiente.Observaciones?.Trim(), LONGITUD_OBSERVACIONES);
            propuesta.Vendedor = pendiente.Vendedor?.Trim();
            propuesta.Fecha = pendiente.Fecha;
            propuesta.FechaEntrega = pendiente.FechaEntrega;
            propuesta.Pais = pendiente.Pais > 0 ? pendiente.Pais : PAIS_ESPANA;
            propuesta.PaisIso = PaisIsoDe(propuesta.Pais, propuesta.CodPostal);
            // El reembolso del pendiente (CrearEtiquetaPendiente usa -1 = «no cobrar»; al tramitar pasa a 0, #135).
            propuesta.Reembolso = pendiente.Reembolso < 0 ? 0m : pendiente.Reembolso;

            // VB 586-594 + 600-607 (Nesto#495): servicio y retorno del pendiente si existen en su agencia; si no, el defecto.
            var defectos = DefaultsDe(pendiente.Agencia, propuesta.CodPostal);
            CatalogoServiciosAgencia catalogo = CatalogoServiciosAgencias.De(pendiente.Agencia);
            propuesta.Servicio = catalogo == null || catalogo.Servicios.ContainsKey(pendiente.Servicio) ? pendiente.Servicio : defectos.Servicio;
            propuesta.Horario = catalogo == null || catalogo.Horarios.ContainsKey(pendiente.Horario) ? pendiente.Horario : defectos.Horario;
            propuesta.Retorno = catalogo == null || catalogo.Retornos.ContainsKey(pendiente.Retorno) ? pendiente.Retorno : (short)0;

            // VB 3513-3517 (#238): ImporteGasto con el coste real si se tiene; si no, el que tuviera.
            decimal coste = CosteDeAgencia(empresa, propuesta, reembolsoPedido);
            propuesta.ImporteGasto = coste > 0 ? coste : pendiente.ImporteGasto;
        }

        // ---------------------------------------------------------------- nuevo / ampliación

        private async Task RellenarNuevoOAmpliacion(PropuestaEnvioDTO propuesta, PedidoParaAgenciaDTO pedido, ClienteParaAgenciaDTO ficha,
            EnviosAgencia ampliacion, string empresa, decimal reembolsoPedido, List<AgenciaTransporte> agencias)
        {
            // VB 499-515: destino = ficha del cliente/contacto del pedido.
            string nombre = ficha.Nombre?.Trim() ?? string.Empty;
            var telefono = new Telefono(ficha.Telefono);
            // VB 3531-3541: comillas fuera de nombre, dirección y observaciones; atención = nombre (attEnvio, VB 515).
            propuesta.Nombre = nombre.Replace("\"", string.Empty);
            propuesta.Direccion = (ficha.Direccion?.Trim() ?? string.Empty).Replace("\"", string.Empty);
            propuesta.CodPostal = ficha.CodPostal?.Trim() ?? string.Empty;
            propuesta.Poblacion = ficha.Poblacion?.Trim() ?? string.Empty;
            propuesta.Provincia = ficha.Provincia?.Trim() ?? string.Empty;
            propuesta.Telefono = telefono.FijoUnico();
            propuesta.Movil = telefono.MovilUnico();
            propuesta.Email = CorreoUnico(ficha.PersonasContacto);
            propuesta.Atencion = nombre;
            propuesta.Observaciones = Recortar(pedido.Comentarios?.Replace("\"", string.Empty), LONGITUD_OBSERVACIONES);
            // VB 3544: el vendedor del pedido o "NV".
            propuesta.Vendedor = string.IsNullOrWhiteSpace(pedido.Vendedor) ? VENDEDOR_POR_DEFECTO : pedido.Vendedor.Trim();
            // Decisión #595: el país real del destino sale del CP (portugués → PT). Nesto pasa el país de la pantalla, que
            // casi siempre es España (PaisIsoActual, VB 2423).
            string paisIso = PaisIsoDe(0, propuesta.CodPostal);
            propuesta.PaisIso = paisIso;

            int agencia;
            if (ampliacion != null)
            {
                // VB 3439-3441 (Nesto#412): en una ampliación la agencia es la DEL ENVÍO que se amplía.
                propuesta.Origen = PropuestaEnvioDTO.ORIGEN_AMPLIACION;
                propuesta.EnvioOrigen = ampliacion.Numero;
                agencia = ampliacion.Agencia;
            }
            else
            {
                propuesta.Origen = PropuestaEnvioDTO.ORIGEN_NUEVO;
                // VB 3682-3733 + 3740-3758: la más barata del comparador del servidor para el CP real (cuarentena, sombra,
                // zonas activas y Canarias → Canteras van dentro del comparador).
                agencia = AgenciaMasEconomica(empresa, propuesta.CodPostal, propuesta.Peso, reembolsoPedido, paisIso, agencias);
                // NestoAPI#739: los pedidos de ruta propia (AT, 16) van por nuestro reparto, si está encendido y dado de alta.
                if (VaPorRepartoPropio(pedido.Ruta, agencias))
                {
                    agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;
                }
            }
            propuesta.Agencia = agencia;

            // VB 3520: la empresa del envío es la de la agencia.
            AgenciaTransporte filaAgencia = agencias.FirstOrDefault(a => a.Numero == agencia);
            propuesta.Empresa = string.IsNullOrWhiteSpace(filaAgencia?.Empresa) ? empresa?.Trim() : filaAgencia.Empresa.Trim();

            // VB 516 + 3525-3526: fecha = FechaPicking de la empresa (o hoy) y se entrega al día siguiente.
            DateTime? fechaPicking = await datos.FechaPicking(propuesta.Empresa).ConfigureAwait(false);
            propuesta.Fecha = (fechaPicking ?? DateTime.Today).Date;
            propuesta.FechaEntrega = propuesta.Fecha.AddDays(1);

            // VB 328-333: servicio, horario y país por defecto de la agencia; retorno «sin retorno» (0 en todas).
            var defectos = DefaultsDe(agencia, propuesta.CodPostal);
            propuesta.Servicio = defectos.Servicio;
            propuesta.Horario = defectos.Horario;
            propuesta.Retorno = 0;
            propuesta.Pais = paisIso == "PT" && AdmitePortugal(agencia) ? PAIS_PORTUGAL : defectos.Pais;

            // VB 3542: en una ampliación de OTRO pedido se suma el reembolso al que ya llevaba el envío.
            propuesta.Reembolso = ampliacion != null && ampliacion.Pedido != pedido.Numero
                ? ampliacion.Reembolso + reembolsoPedido
                : reembolsoPedido;

            // VB 3546-3549 + 2374-2384 (#238): coste de la agencia realmente usada; 0 si no se puede.
            propuesta.ImporteGasto = CosteDeAgencia(empresa, propuesta, reembolsoPedido);
        }

        /// <summary>
        /// NestoAPI#739: ¿se propone el reparto propio? Solo con el interruptor encendido, la agencia 14 dada de alta
        /// (Scripts/Issue739_RepartoPropio.sql) y un pedido de ruta propia (las de Facturar rutas: AT y 16).
        /// </summary>
        internal bool VaPorRepartoPropio(string ruta, IEnumerable<AgenciaTransporte> agencias)
            => RepartoPropio.RutasRepartoPropio.EsRutaPropia(ruta)
            && (agencias ?? Enumerable.Empty<AgenciaTransporte>()).Any(a => a.Numero == Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            && RepartoPropioActivo();

        private int AgenciaMasEconomica(string empresa, string codPostal, decimal peso, decimal reembolso, string paisIso,
            List<AgenciaTransporte> agencias)
        {
            OpcionEnvioAgencia mejor = datos.Comparador().MasEconomica(empresa, codPostal, peso, reembolso, paisIso);
            if (mejor == null)
            {
                return 0;
            }
            return AgenciaDeLaEmpresa(mejor.AgenciaId, empresa, agencias);
        }

        /// <summary>
        /// VB 3713-3731 (Nesto#475): las tarifas llevan el número de agencia de la empresa 1 (ASM = 1). En un pedido de
        /// otra empresa (la espejo) se busca la misma agencia POR NOMBRE entre las de esa empresa (ASM = 5).
        /// </summary>
        internal static int AgenciaDeLaEmpresa(int agenciaComparador, string empresa, IEnumerable<AgenciaTransporte> agencias)
        {
            List<AgenciaTransporte> lista = (agencias ?? Enumerable.Empty<AgenciaTransporte>()).ToList();
            AgenciaTransporte delComparador = lista.FirstOrDefault(a => a.Numero == agenciaComparador);
            string empresaPedido = empresa?.Trim() ?? string.Empty;
            if (delComparador == null || string.IsNullOrEmpty(empresaPedido) ||
                string.Equals(delComparador.Empresa?.Trim(), empresaPedido, StringComparison.OrdinalIgnoreCase))
            {
                return agenciaComparador;
            }
            AgenciaTransporte mismaEnLaEmpresa = lista.FirstOrDefault(a =>
                string.Equals(a.Empresa?.Trim(), empresaPedido, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Nombre?.Trim(), delComparador.Nombre?.Trim(), StringComparison.OrdinalIgnoreCase));
            return mismaEnLaEmpresa?.Numero ?? agenciaComparador;
        }

        // ---------------------------------------------------------------- avisos

        private async Task AnadirAvisos(PropuestaEnvioDTO propuesta, string empresa, int pedido)
        {
            // VB 3478-3488: «Este pedido no tiene ninguna línea con picking. ¿Desea insertar el pedido de todos modos?»
            if (!await datos.TieneAlgunaLineaConPicking(empresa, pedido).ConfigureAwait(false))
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.SIN_PICKING, TiposAvisoPropuestaEnvio.CONFIRMAR, "Este pedido no tiene ninguna línea con picking.");
            }
            // VB 3449-3472: la ampliación se confirma con los bultos totales.
            if (propuesta.Origen == PropuestaEnvioDTO.ORIGEN_AMPLIACION)
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.AMPLIACION, TiposAvisoPropuestaEnvio.CONFIRMAR, $"Este pedido es una ampliación del envío {propuesta.EnvioOrigen}: se actualizarían los datos de ese envío " +
                    $"y el nº total de bultos pasaría a ser {propuesta.Bultos}.");
            }
            // NestoAPI#686 (pedido 928220): al contado con «Mantener junto» y líneas pendientes, este envío sale sin
            // reembolso (no hay factura todavía). No pasa en silencio: se pregunta y queda en ELMAH.
            if (LeerSaleSinReembolsoPorMantenerJunto(empresa, pedido))
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.CONTADO_MANTENER_JUNTO, TiposAvisoPropuestaEnvio.CONFIRMAR, MantenerJuntoContado.AVISO_ENVIO_SIN_REEMBOLSO);
                ReembolsoMantenerJuntoInfo.Registrar(pedido);
            }
            AnadirAvisosDeDestino(propuesta);
        }

        private bool LeerSaleSinReembolsoPorMantenerJunto(string empresa, int pedido)
        {
            try
            {
                return datos.SaleSinReembolsoPorMantenerJunto(empresa, pedido);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"Propuesta de envío del pedido {pedido}: no se pudo comprobar «Mantener junto» al contado (#686). {ex.Message}", ex));
                return false;
            }
        }

        // Los avisos que dependen de lo que se puede cambiar en la pantalla (destino, agencia, reembolso).
        private static readonly string[] CodigosAvisosDeDestino =
        {
            CodigosAvisoPropuestaEnvio.DIRECCION_PROPIA, CodigosAvisoPropuestaEnvio.PREPAGOS,
            CodigosAvisoPropuestaEnvio.DIMENSIONES, CodigosAvisoPropuestaEnvio.SIN_AGENCIA,
            CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE, CodigosAvisoPropuestaEnvio.CONSULTA_REEMBOLSO_ABIERTA
        };

        /// <summary>
        /// NestoAPI#595 (slice 2, corte 4): vuelve a calcular los avisos que dependen del destino, la agencia y el reembolso
        /// (dirección propia, prepagos, dimensiones y sin agencia) después de aplicar los cambios de la pantalla. Los de sin
        /// picking y ampliación no cambian.
        /// </summary>
        internal static void RecalcularAvisosDeDestino(PropuestaEnvioDTO propuesta)
        {
            for (int i = propuesta.AvisosDetalle.Count - 1; i >= 0; i--)
            {
                AvisoPropuestaEnvio aviso = propuesta.AvisosDetalle[i];
                if (CodigosAvisosDeDestino.Contains(aviso.Codigo))
                {
                    propuesta.AvisosDetalle.RemoveAt(i);
                    _ = propuesta.Avisos.Remove(aviso.Texto);
                }
            }
            AnadirAvisosDeDestino(propuesta);
        }

        private static void AnadirAvisosDeDestino(PropuestaEnvioDTO propuesta)
        {
            // VB 2002-2034 (ConfirmarDireccionPropia): envío a la dirección de Nueva Visión.
            if (EsDireccionDeNuevaVision(propuesta.Direccion, propuesta.CodPostal))
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.DIRECCION_PROPIA, TiposAvisoPropuestaEnvio.CONFIRMAR, $"El envío va a la dirección de Nueva Visión: {propuesta.Direccion}, {propuesta.CodPostal} {propuesta.Poblacion}. " +
                    "Si es un pedido de la tienda online, la dirección del cliente está en los comentarios del pedido.");
            }
            // VB 669-680 (#569): HayAvisoPrepagos = prepagos > 0 y reembolso > 0.
            // NestoAPI#679: lo cobrado con enlaces de pago va dentro de los prepagos y se dice cuánto es.
            if (propuesta.PrepagosPendientes > 0m && propuesta.Reembolso > 0m)
            {
                string conEnlace = propuesta.EnlacesPagoAutorizados > 0m
                    ? string.Format(Espanol, " ({0:C} con enlace de pago)", Math.Min(propuesta.EnlacesPagoAutorizados, propuesta.PrepagosPendientes))
                    : string.Empty;
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.PREPAGOS, TiposAvisoPropuestaEnvio.ELEGIR, string.Format(Espanol, "El pedido tiene {0:C} pagados por adelantado{2}. El reembolso propuesto ({1:C}) no los descuenta.",
                    propuesta.PrepagosPendientes, propuesta.Reembolso, conEnlace));
            }
            // NestoAPI#734: consultado a Administración y sin contestar: el pedido está retenido hasta que contesten.
            if (propuesta.ConsultaReembolso?.Abierta == true)
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.CONSULTA_REEMBOLSO_ABIERTA, TiposAvisoPropuestaEnvio.BLOQUEANTE,
                    TextosConsultaReembolso.AvisoPropuestaAbierta(propuesta.ConsultaReembolso));
            }
            // NestoAPI#679 (pedido 928521): con un enlace de pago sin pagar, ¿seguro que sale contra reembolso? NestoAPI#734: el
            // cliente puede ofrecer «Retener y consultar a Administración»; si Administración ya decidió, no se vuelve a avisar.
            else if (propuesta.Reembolso > 0m && propuesta.EnlacesPagoPendientes != null && propuesta.EnlacesPagoPendientes.Count > 0
                && !DecisionCubreLosEnlaces(propuesta.ConsultaReembolso, propuesta.EnlacesPagoPendientes))
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE, TiposAvisoPropuestaEnvio.CONFIRMAR,
                    EnlacesPagoPedido.TextoAvisoPendientes(propuesta.EnlacesPagoPendientes), AccionesAvisoPropuestaEnvio.CONSULTAR_ADMINISTRACION);
            }
            // VB 2110-2124 (Nesto#367): Canteras (DimensionesBultosObligatorias) pide las dimensiones al imprimir.
            if (propuesta.Agencia == Constantes.Agencias.AGENCIA_CANTERAS)
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.DIMENSIONES, TiposAvisoPropuestaEnvio.DATO, "Canteras necesita las dimensiones de los bultos (AnchoxAltoxLargo, por ejemplo 30x20x15).");
            }
            // VB 2407-2409: sin agencia con tarifa para el destino no se puede tramitar.
            if (propuesta.Agencia == 0)
            {
                Avisar(propuesta, CodigosAvisoPropuestaEnvio.SIN_AGENCIA, TiposAvisoPropuestaEnvio.BLOQUEANTE, $"Ninguna agencia tiene tarifa para la zona del destino (CP {propuesta.CodPostal}).");
            }
        }

        /// <summary>NestoAPI#595 (slice 2, 20/10/26): el aviso va a Avisos (el texto, como siempre) y a AvisosDetalle.</summary>
        private static void Avisar(PropuestaEnvioDTO propuesta, string codigo, string tipo, string texto, string accion = null)
        {
            propuesta.Avisos.Add(texto);
            propuesta.AvisosDetalle.Add(new AvisoPropuestaEnvio { Codigo = codigo, Texto = texto, Tipo = tipo, Accion = accion });
        }

        /// <summary>
        /// NestoAPI#734: Administración ya contestó y los enlaces pendientes son los que había entonces. Un enlace enviado
        /// DESPUÉS de la respuesta es una situación nueva: se vuelve a avisar.
        /// </summary>
        internal static bool DecisionCubreLosEnlaces(ConsultaReembolsoDTO consulta, IEnumerable<EnlacePagoPedido> pendientes)
        {
            if (consulta == null || !consulta.Respondida.HasValue)
            {
                return false;
            }
            DateTime respondida = consulta.Respondida.Value;
            return (pendientes ?? Enumerable.Empty<EnlacePagoPedido>()).All(e => e.FechaCreacion <= respondida);
        }

        private async Task<ConsultaReembolsoDTO> LeerConsultaReembolso(string empresa, int pedido)
        {
            if (leerConsultaReembolso == null)
            {
                return null;
            }
            try
            {
                return await leerConsultaReembolso(empresa, pedido).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"Propuesta de envío del pedido {pedido}: no se pudo leer la consulta de reembolso (#734). {ex.Message}", ex));
                return null;
            }
        }

        // ---------------------------------------------------------------- piezas

        private decimal LeerPrepagos(string empresa, int pedido)
        {
            try
            {
                return datos.PrepagosPendientes(empresa, pedido);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"Propuesta de envío del pedido {pedido}: no se pudieron leer los prepagos. {ex.Message}", ex));
                return 0m;
            }
        }

        private List<EnlacePagoPedido> LeerEnlacesDePago(string empresa, int pedido)
        {
            try
            {
                return datos.EnlacesDePago(empresa, pedido) ?? new List<EnlacePagoPedido>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"Propuesta de envío del pedido {pedido}: no se pudieron leer los enlaces de pago (#679). {ex.Message}", ex));
                return new List<EnlacePagoPedido>();
            }
        }

        private async Task<short> BultosDelPacking(string empresa, int pedido)
        {
            try
            {
                int deAriadna = await datos.BultosPacking(empresa, pedido).ConfigureAwait(false);
                return deAriadna > 0 ? (short)Math.Min(deAriadna, short.MaxValue) : (short)1;
            }
            catch (Exception ex)
            {
                // Como ProponerBultosAriadnaAsync (VB 3085-3101): si falla, todo sigue como antes.
                ElmahHelper.Log(new Exception($"Propuesta de envío del pedido {pedido}: no se pudieron leer los bultos de Ariadna. {ex.Message}", ex));
                return 1;
            }
        }

        /// <summary>NestoAPI#595 (slice 2): Nesto#367, el formato de las dimensiones de los bultos (EsFormatoDimensionesValido).</summary>
        internal static bool EsFormatoDimensionesValido(string texto)
            => !string.IsNullOrWhiteSpace(texto) &&
               Regex.IsMatch(texto, @"\d+([.,]\d+)?\s*[xX×]\s*\d+([.,]\d+)?\s*[xX×]\s*\d+([.,]\d+)?");

        /// <summary>NestoAPI#595 (slice 2): Nesto#367, las dimensiones se añaden a las observaciones sin pisarlas, y todo a 80.</summary>
        internal static string ObservacionesConDimensiones(string observaciones, string dimensiones)
        {
            string combinadas = string.IsNullOrWhiteSpace(observaciones)
                ? dimensiones?.Trim()
                : $"{observaciones.Trim()} - {dimensiones?.Trim()}";
            return Recortar(combinadas, LONGITUD_OBSERVACIONES);
        }

        /// <summary>NestoAPI#595 (slice 2): observaciones de la pantalla, sin comillas y como mucho 80 (VB 3541).</summary>
        internal static string ObservacionesDeLaPantalla(string observaciones)
            => Recortar(observaciones?.Replace("\"", string.Empty), LONGITUD_OBSERVACIONES);

        private decimal CosteDeAgencia(string empresa, PropuestaEnvioDTO propuesta, decimal reembolsoPedido)
        {
            if (propuesta.Agencia <= 0)
            {
                return 0m;
            }
            try
            {
                OpcionEnvioAgencia opcion = datos.Comparador().CosteDeAgencia(empresa, propuesta.CodPostal, propuesta.Peso, reembolsoPedido,
                    propuesta.Agencia, ServicioForzadoParaCoste(propuesta.Agencia, propuesta.Servicio, propuesta.CodPostal), propuesta.PaisIso);
                return opcion?.Coste ?? 0m;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"Propuesta de envío del pedido {propuesta.Pedido}: no se pudo calcular el coste de la agencia {propuesta.Agencia}. {ex.Message}", ex));
                return 0m;
            }
        }

        /// <summary>
        /// VB 612-616 (#505): solo se pide un servicio concreto al calcular el coste cuando, en una agencia gestionada por
        /// la API (CTT, Innovatrans), no es el de por defecto (CTT 24 h); sin él, el más barato de la agencia.
        /// </summary>
        internal static byte? ServicioForzadoParaCoste(int agencia, short servicio, string codPostal)
        {
            bool gestionadaPorApi = agencia == Constantes.Agencias.AGENCIA_CTT || agencia == Constantes.Agencias.AGENCIA_INNOVATRANS;
            if (!gestionadaPorApi || servicio == DefaultsDe(agencia, codPostal).Servicio || servicio < 0 || servicio > byte.MaxValue)
            {
                return null;
            }
            return (byte)servicio;
        }

        /// <summary>
        /// Defaults de la agencia: su perfil (IPerfilConDefaultsEnvio) o, si no tiene, los de Nesto para las agencias sin
        /// perfil de defaults (AgenciaGestionadaPorApi.vb e AgenciaCanteras.vb: servicio 0, horario 0, España).
        /// </summary>
        internal static (short Servicio, short Horario, int Pais) DefaultsDe(int agencia, string codPostal)
        {
            return (_perfiles.Perfil(agencia) as IPerfilConDefaultsEnvio)?.DefaultsEnvio(codPostal ?? string.Empty)
                ?? (Servicio: (short)0, Horario: (short)0, Pais: PAIS_ESPANA);
        }

        // Las agencias cuya lista de países de Nesto incluye Portugal (AgenciaASM.rellenarPaises, PaisesSoportados de
        // AgenciaGestionadaPorApi).
        private static bool AdmitePortugal(int agencia)
            => agencia == Constantes.Agencias.AGENCIA_GLS || agencia == Constantes.Agencias.AGENCIA_CTT
            || agencia == Constantes.Agencias.AGENCIA_INNOVATRANS;

        internal static string PaisIsoDe(int pais, string codPostal)
        {
            if (pais == PAIS_PORTUGAL)
            {
                return "PT";
            }
            // NestoAPI#596: la forma portuguesa la decide Direcciones.CodigoPostal (con o sin guion/espacio).
            return Direcciones.CodigoPostal.TieneFormatoPortugues(codPostal) ? "PT" : "ES";
        }

        /// <summary>CorreoCliente (el mismo criterio que Nesto, VB 3321-3338): primero el de agencia, luego cualquiera.</summary>
        internal static string CorreoUnico(IEnumerable<PersonaContactoAgenciaDTO> personas)
        {
            List<PersonaContactoCliente> lista = (personas ?? Enumerable.Empty<PersonaContactoAgenciaDTO>())
                .Select(p => new PersonaContactoCliente { Cargo = p.Cargo, CorreoElectrónico = p.CorreoElectronico })
                .ToList();
            return new CorreoCliente(lista).CorreoAgencia();
        }

        /// <summary>
        /// VB 2021-2034 (EsDireccionDeNuevaVision): Río Tiétar, Algete (28110, o el 28119 que da Google para la nave),
        /// con o sin tildes.
        /// </summary>
        internal static bool EsDireccionDeNuevaVision(string direccion, string codPostal)
        {
            if (string.IsNullOrWhiteSpace(direccion) || string.IsNullOrWhiteSpace(codPostal))
            {
                return false;
            }
            if (codPostal.Trim() != "28110" && codPostal.Trim() != "28119")
            {
                return false;
            }
            string sinTildes = new string(direccion.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray()).ToUpperInvariant();
            return sinTildes.Contains("TIETAR");
        }

        private static string Recortar(string valor, int maximo)
        {
            if (string.IsNullOrEmpty(valor))
            {
                return string.Empty;
            }
            return valor.Length <= maximo ? valor : valor.Substring(0, maximo);
        }
    }

    /// <summary>
    /// NestoAPI#595: sombra de la propuesta. Compara campo a campo el envío que ha grabado Nesto con la propuesta del
    /// servidor. Puro, para probarlo sin BD.
    /// </summary>
    public static class ComparadorPropuestaEnvio
    {
        public const decimal TOLERANCIA_IMPORTE_GASTO = 0.01m;

        public static List<string> Diferencias(EnviosAgencia envio, PropuestaEnvioDTO propuesta)
        {
            var diferencias = new List<string>();
            if (envio == null || propuesta == null)
            {
                return diferencias;
            }
            if (propuesta.Origen != PropuestaEnvioDTO.ORIGEN_NUEVO)
            {
                diferencias.Add($"origen: Nesto={PropuestaEnvioDTO.ORIGEN_NUEVO} / propuesta={propuesta.Origen} {propuesta.EnvioOrigen}");
            }
            Comparar(diferencias, "agencia", envio.Agencia, propuesta.Agencia);
            Comparar(diferencias, "servicio", envio.Servicio, propuesta.Servicio);
            Comparar(diferencias, "horario", envio.Horario, propuesta.Horario);
            Comparar(diferencias, "retorno", envio.Retorno, propuesta.Retorno);
            Comparar(diferencias, "reembolso", envio.Reembolso, propuesta.Reembolso);
            Comparar(diferencias, "nombre", envio.Nombre, propuesta.Nombre);
            Comparar(diferencias, "dirección", envio.Direccion, propuesta.Direccion);
            Comparar(diferencias, "CP", envio.CodPostal, propuesta.CodPostal);
            Comparar(diferencias, "población", envio.Poblacion, propuesta.Poblacion);
            Comparar(diferencias, "provincia", envio.Provincia, propuesta.Provincia);
            Comparar(diferencias, "país", envio.Pais, propuesta.Pais);
            Comparar(diferencias, "teléfono", envio.Telefono, propuesta.Telefono);
            Comparar(diferencias, "móvil", envio.Movil, propuesta.Movil);
            Comparar(diferencias, "email", envio.Email, propuesta.Email);
            Comparar(diferencias, "atención", envio.Atencion, propuesta.Atencion);
            Comparar(diferencias, "vendedor", envio.Vendedor, propuesta.Vendedor);
            Comparar(diferencias, "bultos", envio.Bultos, propuesta.Bultos);
            Comparar(diferencias, "peso", envio.Peso, propuesta.Peso);
            // NestoAPI#595 (slice 2, 20/10/26): también fechas (solo el día), observaciones (espacios normalizados) y empresa.
            CompararDia(diferencias, "fecha", envio.Fecha, propuesta.Fecha);
            CompararDia(diferencias, "fecha de entrega", envio.FechaEntrega, propuesta.FechaEntrega);
            Comparar(diferencias, "observaciones", NormalizarEspacios(envio.Observaciones), NormalizarEspacios(propuesta.Observaciones));
            Comparar(diferencias, "empresa", envio.Empresa, propuesta.Empresa);
            if (Math.Abs(envio.ImporteGasto - propuesta.ImporteGasto) > TOLERANCIA_IMPORTE_GASTO)
            {
                diferencias.Add(string.Format(CultureInfo.InvariantCulture, "ImporteGasto: Nesto={0} / propuesta={1}", envio.ImporteGasto, propuesta.ImporteGasto));
            }
            return diferencias;
        }

        private static void Comparar(List<string> diferencias, string campo, string nesto, string propuesta)
        {
            string a = nesto?.Trim() ?? string.Empty;
            string b = propuesta?.Trim() ?? string.Empty;
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                diferencias.Add($"{campo}: Nesto={a} / propuesta={b}");
            }
        }

        private static void CompararDia(List<string> diferencias, string campo, DateTime? nesto, DateTime? propuesta)
        {
            if (nesto?.Date != propuesta?.Date)
            {
                diferencias.Add($"{campo}: Nesto={Dia(nesto)} / propuesta={Dia(propuesta)}");
            }
        }

        private static string Dia(DateTime? fecha) => fecha?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "(sin fecha)";

        /// <summary>Sin espacios al principio ni al final y cada tramo de espacios (o saltos de línea) como un solo espacio.</summary>
        internal static string NormalizarEspacios(string texto)
            => string.IsNullOrWhiteSpace(texto) ? string.Empty : Regex.Replace(texto.Trim(), @"\s+", " ");

        private static void Comparar<T>(List<string> diferencias, string campo, T nesto, T propuesta) where T : IEquatable<T>
        {
            if (!nesto.Equals(propuesta))
            {
                diferencias.Add(string.Format(CultureInfo.InvariantCulture, "{0}: Nesto={1} / propuesta={2}", campo, nesto, propuesta));
            }
        }
    }

    /// <summary>NestoAPI#686: aviso informativo (no es un error): un envío al contado con «Mantener junto» sale sin reembolso.</summary>
    public class ReembolsoMantenerJuntoInfo : Exception
    {
        public ReembolsoMantenerJuntoInfo(string mensaje) : base(mensaje) { }

        public static void Registrar(int pedido)
            => ElmahHelper.Log(new ReembolsoMantenerJuntoInfo($"[NestoAPI#686] Pedido {pedido}: es al contado con «Mantener junto» y aún " +
                "tiene líneas pendientes, así que el envío sale sin reembolso. Se cobrará todo con el envío que complete el pedido."));
    }

    /// <summary>
    /// NestoAPI#595: aviso informativo (no es un error) de que la propuesta del servidor no coincide con el envío que ha
    /// grabado Nesto. Las coincidencias NO se registran.
    /// </summary>
    public class PropuestaEnvioDifiereInfo : Exception
    {
        public PropuestaEnvioDifiereInfo(string mensaje) : base(mensaje) { }

        public static PropuestaEnvioDifiereInfo Crear(EnviosAgencia envio, IReadOnlyCollection<string> diferencias)
            => new PropuestaEnvioDifiereInfo($"[Propuesta de envío #595] Envío {envio.Numero} (pedido {envio.Pedido}): " +
                $"la propuesta del servidor difiere de lo que ha grabado Nesto en {diferencias.Count} " +
                $"{(diferencias.Count == 1 ? "campo" : "campos")}: {string.Join("; ", diferencias)}");
    }

    /// <summary>Los datos de la propuesta desde la BD, reutilizando los gestores que ya sirven a Agencias de Nesto.</summary>
    public class DatosPropuestaEnvioEF : IDatosPropuestaEnvio
    {
        private readonly NVEntities db;
        private readonly Func<IServicioPedidosVenta> servicioPedidosVenta;
        private ComparadorAgencias comparador;

        /// <param name="servicioPedidosVenta">Perezoso: ServicioPedidosVenta abre su propia conexión al construirse.</param>
        public DatosPropuestaEnvioEF(NVEntities db, Func<IServicioPedidosVenta> servicioPedidosVenta)
        {
            this.db = db;
            this.servicioPedidosVenta = servicioPedidosVenta;
        }

        public Task<PedidoParaAgenciaDTO> LeerPedido(string empresa, int pedido)
            => new GestorPedidoParaAgencia(db).LeerPorEmpresaYNumero(empresa, pedido);

        public async Task<bool> TieneAlgunaLineaConPicking(string empresa, int pedido)
            => (await new GestorPedidoParaAgencia(db).LeerSituacionLineas(empresa, pedido).ConfigureAwait(false)).TieneAlgunaLineaConPicking;

        public Task<EnviosAgencia> LeerEnvioPendiente(string empresa, int pedido)
            => db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Estado < Constantes.Agencias.ESTADO_EN_CURSO && e.Empresa == empresa && e.Pedido == pedido)
                .OrderBy(e => e.Numero)
                .FirstOrDefaultAsync();

        public Task<EnviosAgencia> LeerEnvioAmpliacion(string cliente, string contacto, string direccion, int? excluirEnvio)
        {
            int excluido = excluirEnvio ?? 0;
            return db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Estado == Constantes.Agencias.ESTADO_EN_CURSO &&
                            e.Cliente == cliente && e.Contacto == contacto && e.Direccion == direccion &&
                            e.Numero != excluido)
                .OrderBy(e => e.Numero)
                .FirstOrDefaultAsync();
        }

        public decimal ImporteReembolso(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).ImporteReembolso(empresa, pedido);

        public decimal PrepagosPendientes(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).ImportePrepagosPendientes(empresa, pedido);

        public List<EnlacePagoPedido> EnlacesDePago(string empresa, int pedido)
            => LectorEnlacesPagoPedido.Leer(db, empresa, pedido);

        public bool SaleSinReembolsoPorMantenerJunto(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).SaleSinReembolsoPorMantenerJunto(empresa, pedido);

        public async Task<int> BultosPacking(string empresa, int pedido)
        {
            List<BultoAlmacenDTO> bultos = await new RepositorioPreparacionAlmacen(db).LeerBultos(empresa, pedido).ConfigureAwait(false);
            // PropuestaBultosAriadna.NumeroDeBultos (Nesto#507): bultos distintos por Id.
            return (bultos ?? new List<BultoAlmacenDTO>()).Select(b => b.Id).Distinct().Count();
        }

        public Task<List<AgenciaTransporte>> LeerAgencias()
            => db.AgenciasTransportes.AsNoTracking().ToListAsync();

        public Task<DateTime?> FechaPicking(string empresa)
            => db.Empresas.AsNoTracking().Where(e => e.Número == empresa).Select(e => e.FechaPicking).FirstOrDefaultAsync();

        public ComparadorAgencias Comparador()
            => comparador ?? (comparador = ComparadorAgenciasFactory.ParaSeleccion(db));
    }
}
