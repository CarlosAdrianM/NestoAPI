using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#758: qué ha pasado con la parada de una nota al facturar la ruta.</summary>
    public enum AccionParadaNotaEntrega
    {
        /// <summary>No es ruta de reparto propio (o el interruptor Agencias:RepartoPropio está apagado): como hasta ahora.</summary>
        NoAplica,
        /// <summary>Ya tenía su envío de la agencia 14 (el del packing, o el de una facturación anterior).</summary>
        Reutilizada,
        /// <summary>Se ha creado el envío de la agencia 14, ya tramitado (recogida, o entrega que no ha pasado por packing).</summary>
        Creada,
        /// <summary>El packing la mandó por otra agencia: no se toca y no es parada (se anota en ELMAH).</summary>
        OtraAgencia,
        /// <summary>Tiene un envío de la agencia 14 sin tramitar: no sale en el reparto hasta que se tramite.</summary>
        SinTramitar
    }

    /// <summary>NestoAPI#758: el resultado de <see cref="IServicioParadasNotaEntrega.AsegurarParada"/>.</summary>
    public class ResultadoParadaNotaEntrega
    {
        public AccionParadaNotaEntrega Accion { get; set; }
        /// <summary>«Entrega» o «Recogida».</summary>
        public string Tipo { get; set; } = TiposNotaEntrega.ENTREGA;
        /// <summary>NE{pedido} / NR{pedido}.</summary>
        public string CodigoDocumento { get; set; }
        /// <summary>Recogida: los de la cabecera. Entrega: los de su envío (null sin envío).</summary>
        public short? Bultos { get; set; }
        /// <summary>El envío que es la parada (o el de la otra agencia / el sin tramitar), o null.</summary>
        public int? EnvioId { get; set; }
        /// <summary>Lo que tiene que ver quien factura la ruta (de usted), o null si todo ha ido bien.</summary>
        public string Aviso { get; set; }

        public bool EsParada => Accion == AccionParadaNotaEntrega.Reutilizada || Accion == AccionParadaNotaEntrega.Creada;
        public bool EsRecogida => Tipo == TiposNotaEntrega.RECOGIDA;
    }

    public interface IServicioParadasNotaEntrega
    {
        /// <summary>¿La ruta es de reparto propio (parámetro RutasRepartoPropio) y el reparto propio está encendido?</summary>
        bool EsRepartoPropio(string ruta);

        /// <summary>
        /// Facturar rutas, después de procesar la nota sin error: la nota de una ruta de reparto propio entra en el reparto como
        /// parada (un envío de la agencia 14 tramitado). Idempotente: relanzarlo no crea otra.
        /// </summary>
        Task<ResultadoParadaNotaEntrega> AsegurarParada(CabPedidoVta pedido, string usuario);

        /// <summary>«NE930123» / «NR930123» / «NR930123-2» → el envío de su parada (agencia 14, Tramitado o Incidentado), o null.</summary>
        Task<int?> ResolverParada(string codigo);

        /// <summary>
        /// NestoAPI#759: como <see cref="ResolverParada"/>, pero diciendo por qué no hay parada (no es una nota, no está en el
        /// reparto o ya está entregada), para dar el 404/409 de usted al escanear. Null si el código no es NE/NR.
        /// </summary>
        Task<ResolucionCodigoNota> Resolver(string codigo);
    }

    /// <summary>NestoAPI#759: lo que se sabe de un código NE/NR escaneado.</summary>
    public class ResolucionCodigoNota
    {
        public int Pedido { get; set; }
        /// <summary>1 en NE{Pedido} y NR{Pedido}; N en NR{Pedido}-{N}.</summary>
        public int Bulto { get; set; } = 1;
        /// <summary>El prefijo es NR.</summary>
        public bool EsRecogida { get; set; }
        /// <summary>El pedido existe en la empresa 1 y es una nota de entrega.</summary>
        public bool EsNota { get; set; }
        /// <summary>El envío de su parada (agencia 14, Tramitado o Incidentado), o null.</summary>
        public int? EnvioId { get; set; }
        /// <summary>Sin parada activa, pero con un envío de la agencia 14 ya entregado.</summary>
        public int? EnvioEntregado { get; set; }
    }

    /// <summary>
    /// NestoAPI#758 (corte 2 de #755), con los ajustes de Carlos del 30/10/26:
    /// <list type="bullet">
    /// <item>ENTREGA: pasa por picking y packing como cualquier pedido; su envío, bultos, etiqueta y reembolso salen del packing.
    /// Al facturar la ruta se reutiliza ese envío de la agencia 14. Mientras las notas de solo texto no entren en el packing
    /// (trabajo aparte), si llega sin envío se le crea la parada igual que a una recogida (1 bulto, reembolso 0) con la historia
    /// «Nota de entrega sin packing en reparto propio», para que no se quede fuera del reparto.</item>
    /// <item>RECOGIDA: no pasa por packing. Se le crea la parada (agencia 14, Tramitado) con los bultos de NotasEntregaCabecera
    /// (mínimo 1) y reembolso 0; sus etiquetas NR{pedido}-{N} las saca <see cref="ServicioEtiquetasRecogida"/>.</item>
    /// <item>Si el envío del packing es de OTRA agencia, no se toca: se anota en ELMAH y no es parada.</item>
    /// </list>
    /// </summary>
    public class ServicioParadasNotaEntrega : IServicioParadasNotaEntrega
    {
        public const string HISTORIA_RECOGIDA = "Nota de recogida en reparto propio";
        public const string HISTORIA_ENTREGA_SIN_PACKING = "Nota de entrega sin packing en reparto propio";
        internal const int LONGITUD_OBSERVACIONES = 80;

        private static readonly short[] ESTADOS_PARADA =
        {
            Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_INCIDENTADO, Constantes.Agencias.ESTADO_ENTREGADO
        };
        private static readonly short[] ESTADOS_ACTIVOS =
        {
            (short)Constantes.Agencias.ESTADO_EN_CURSO, Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_INCIDENTADO
        };

        private readonly IRepositorioParadasNotaEntrega repositorio;
        private readonly IRepositorioNotasEntregaCabecera cabeceras;
        private readonly Func<bool> repartoPropioActivo;
        private readonly Action<Exception> registrar;
        private readonly Func<DateTime> hoy;

        public ServicioParadasNotaEntrega(NVEntities db)
            : this(new RepositorioParadasNotaEntrega(db), new RepositorioNotasEntregaCabecera(db))
        {
        }

        public ServicioParadasNotaEntrega(IRepositorioParadasNotaEntrega repositorio, IRepositorioNotasEntregaCabecera cabeceras,
            Func<bool> repartoPropioActivo = null, Action<Exception> registrar = null, Func<DateTime> hoy = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.cabeceras = cabeceras ?? throw new ArgumentNullException(nameof(cabeceras));
            this.repartoPropioActivo = repartoPropioActivo ?? InterruptorRepartoPropio.EstaActivo;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        public bool EsRepartoPropio(string ruta) => RutasRepartoPropio.EsRutaPropia(ruta) && repartoPropioActivo();

        public async Task<ResultadoParadaNotaEntrega> AsegurarParada(CabPedidoVta pedido, string usuario)
        {
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }
            var resultado = new ResultadoParadaNotaEntrega { Accion = AccionParadaNotaEntrega.NoAplica };
            string empresa = pedido.Empresa?.Trim();
            int numero = pedido.Número;
            FilaNotaEntregaCabecera fila = await cabeceras.Leer(empresa, numero).ConfigureAwait(false);
            resultado.Tipo = TiposNotaEntrega.Normalizar(fila?.Tipo) ?? TiposNotaEntrega.ENTREGA;
            resultado.CodigoDocumento = TiposNotaEntrega.CodigoDocumento(resultado.Tipo, numero);
            short bultosRecogida = (short)Math.Max(1, (int)(fila?.Bultos ?? TiposNotaEntrega.BULTOS_POR_DEFECTO));
            if (resultado.EsRecogida)
            {
                resultado.Bultos = bultosRecogida;
            }
            if (!EsRepartoPropio(pedido.Ruta))
            {
                return resultado; // como hasta ahora: ni parada ni etiquetas
            }

            List<EnvioPedidoNota> envios = await repositorio.LeerEnviosDelPedido(numero).ConfigureAwait(false) ?? new List<EnvioPedidoNota>();

            // 1. Ya es parada (el envío del packing, o el que se creó en una facturación anterior): idempotente.
            EnvioPedidoNota propio = envios.FirstOrDefault(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && ESTADOS_PARADA.Contains(e.Estado));
            if (propio != null)
            {
                resultado.Accion = AccionParadaNotaEntrega.Reutilizada;
                resultado.EnvioId = propio.Numero;
                if (!resultado.EsRecogida)
                {
                    resultado.Bultos = propio.Bultos;
                }
                return resultado;
            }

            // 2. Del reparto propio pero sin tramitar: no se duplica; que lo tramiten.
            EnvioPedidoNota sinTramitar = envios.FirstOrDefault(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                && e.Estado == Constantes.Agencias.ESTADO_EN_CURSO);
            if (sinTramitar != null)
            {
                resultado.Accion = AccionParadaNotaEntrega.SinTramitar;
                resultado.EnvioId = sinTramitar.Numero;
                resultado.Aviso = $"La nota {numero} tiene el envío {sinTramitar.Numero} del reparto propio sin tramitar: tramítelo para que salga en el reparto.";
                return resultado;
            }

            // 3. El packing eligió otra agencia: no se toca.
            EnvioPedidoNota otra = envios.FirstOrDefault(e => e.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO && ESTADOS_ACTIVOS.Contains(e.Estado));
            if (otra != null)
            {
                resultado.Accion = AccionParadaNotaEntrega.OtraAgencia;
                resultado.EnvioId = otra.Numero;
                Registrar(new Exception($"[Notas de entrega #758] La nota {numero} es de la ruta {pedido.Ruta?.Trim()} (reparto propio), pero su envío " +
                    $"{otra.Numero} es de la agencia {otra.Agencia}: no se toca y no entra en el reparto como parada."));
                return resultado;
            }

            // 4. Sin envío: recogida (siempre) o entrega que no ha pasado por packing (de reserva).
            DestinoNotaEntrega destino = await repositorio.LeerDestinoDelPedido(empresa, numero).ConfigureAwait(false);
            if (destino == null)
            {
                resultado.Aviso = $"No se ha podido crear la parada de la nota {numero}: no se encuentra la ficha del cliente.";
                return resultado;
            }
            string empresaAgencia = await repositorio.LeerEmpresaAgencia(Constantes.Agencias.AGENCIA_REPARTO_PROPIO).ConfigureAwait(false);
            short bultos = resultado.EsRecogida ? bultosRecogida : TiposNotaEntrega.BULTOS_POR_DEFECTO;
            EnviosAgencia envio = ConstruirEnvio(pedido, destino, empresaAgencia, bultos, usuario, hoy().Date);
            int creado = await repositorio.CrearEnvioTramitado(envio, resultado.EsRecogida ? HISTORIA_RECOGIDA : HISTORIA_ENTREGA_SIN_PACKING)
                .ConfigureAwait(false);
            resultado.Accion = AccionParadaNotaEntrega.Creada;
            resultado.EnvioId = creado;
            resultado.Bultos = bultos;
            return resultado;
        }

        public async Task<int?> ResolverParada(string codigo) => (await Resolver(codigo).ConfigureAwait(false))?.EnvioId;

        public async Task<ResolucionCodigoNota> Resolver(string codigo)
        {
            if (!CodigoNotaEntrega.TryLeer(codigo, out string prefijo, out int pedido, out int? bulto))
            {
                return null;
            }
            var resolucion = new ResolucionCodigoNota
            {
                Pedido = pedido,
                Bulto = bulto ?? 1,
                EsRecogida = prefijo == CodigoNotaEntrega.PREFIJO_RECOGIDA
            };
            PedidoNotaEntrega cabecera = await cabeceras.LeerPedido(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido).ConfigureAwait(false);
            if (cabecera == null || !cabecera.NotaEntrega)
            {
                return resolucion;
            }
            resolucion.EsNota = true;
            List<EnvioPedidoNota> envios = await repositorio.LeerEnviosDelPedido(pedido).ConfigureAwait(false) ?? new List<EnvioPedidoNota>();
            List<EnvioPedidoNota> propios = envios.Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO).ToList();
            resolucion.EnvioId = propios.FirstOrDefault(e => e.Estado == Constantes.Agencias.ESTADO_TRAMITADO || e.Estado == Constantes.Agencias.ESTADO_INCIDENTADO)?.Numero;
            if (resolucion.EnvioId == null)
            {
                resolucion.EnvioEntregado = propios.FirstOrDefault(e => e.Estado == Constantes.Agencias.ESTADO_ENTREGADO)?.Numero;
            }
            return resolucion;
        }

        /// <summary>Las líneas de texto de la nota, recortadas, sin las vacías y separadas por « - ».</summary>
        public static string TextoNota(IEnumerable<string> textos)
            => string.Join(" - ", (textos ?? Enumerable.Empty<string>()).Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)));

        /// <summary>
        /// El envío de la parada como lo grabaría el packing para la 14 (servicio y horario por defecto de la agencia, datos de la
        /// ficha del cliente/contacto), pero ya tramitado, sin reembolso y con el texto de la nota en las observaciones.
        /// </summary>
        internal static EnviosAgencia ConstruirEnvio(CabPedidoVta pedido, DestinoNotaEntrega destino, string empresaAgencia, short bultos,
            string usuario, DateTime fecha)
        {
            var defectos = PropuestaEnvioService.DefaultsDe(Constantes.Agencias.AGENCIA_REPARTO_PROPIO, destino.CodPostal);
            var telefono = new Telefono(destino.Telefonos);
            string nombre = (destino.Nombre ?? string.Empty).Replace("\"", string.Empty);
            var envio = new EnviosAgencia
            {
                Empresa = string.IsNullOrWhiteSpace(empresaAgencia) ? pedido.Empresa?.Trim() : empresaAgencia,
                Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO,
                Cliente = pedido.Nº_Cliente?.Trim() ?? destino.Cliente,
                Contacto = pedido.Contacto?.Trim() ?? destino.Contacto,
                Pedido = pedido.Número,
                Estado = Constantes.Agencias.ESTADO_TRAMITADO,
                Fecha = fecha,
                FechaEntrega = fecha,
                Servicio = defectos.Servicio,
                Horario = defectos.Horario,
                Retorno = 0,
                Bultos = bultos < 1 ? (short)1 : bultos,
                Nombre = nombre,
                Direccion = (destino.Direccion ?? string.Empty).Replace("\"", string.Empty),
                CodPostal = destino.CodPostal ?? string.Empty,
                Poblacion = destino.Poblacion ?? string.Empty,
                Provincia = destino.Provincia ?? string.Empty,
                Pais = defectos.Pais,
                Telefono = telefono.FijoUnico(),
                Movil = telefono.MovilUnico(),
                Email = string.Empty,
                Atencion = nombre,
                Observaciones = Recortar((destino.Texto ?? string.Empty).Replace("\"", string.Empty), LONGITUD_OBSERVACIONES),
                Reembolso = 0m,
                Vendedor = string.IsNullOrWhiteSpace(pedido.Vendedor) ? (string.IsNullOrWhiteSpace(destino.Vendedor) ? "NV" : destino.Vendedor) : pedido.Vendedor.Trim(),
                Peso = 0m,
                ImporteGasto = 0m,
                Usuario = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario,
                FechaModificacion = DateTime.Now
            };
            envio.CodPostal = EnviosAgenciasController.CodigoPostalEnvio(envio.CodPostal, Direcciones.CodigoPostal.PaisIso(envio.Pais));
            EnviosAgenciasController.RecortarTextosLibres(envio);
            return envio;
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // El registro nunca puede tumbar la facturación de la ruta.
            }
        }
    }
}
