using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#691: un pedido que la API ha creado aparte al guardar (todos los cursos van en un pedido, serie CV).</summary>
    public class PedidoAdicionalDTO
    {
        public int Numero { get; set; }
        /// <summary>«Cursos».</summary>
        public string Motivo { get; set; }
        /// <summary>Lo que ha ido mal sin deshacer el pedido (el enlace de pago, por ejemplo). Va a AvisosCursos, no al JSON.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Aviso { get; set; }
    }

    /// <summary>NestoAPI#691: GET api/Eventos/PorProducto. Un evento futuro de un curso, con lo que se puede vender.</summary>
    public class EventoCursoDTO
    {
        public int Id { get; set; }
        public string Titulo { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>«10:00» o null.</summary>
        public string HoraInicio { get; set; }
        /// <summary>REI o ALC (también los online: el ponente emite desde una tienda).</summary>
        public string Almacen { get; set; }
        /// <summary>«Presencial» u «Online».</summary>
        public string Formato { get; set; }
        /// <summary>Las que quedan; <see cref="ReglasCursosPlantilla.PLAZAS_SIN_LIMITE"/> si el evento no tiene límite.</summary>
        public int PlazasLibres { get; set; }
        public List<ModalidadEventoCursoDTO> Modalidades { get; set; } = new List<ModalidadEventoCursoDTO>();
    }

    /// <summary>NestoAPI#691: una modalidad del evento con su nombre (el de TNV y Nesto) y su importe (el precio de la línea).</summary>
    public class ModalidadEventoCursoDTO
    {
        /// <summary>«Reembolsable» o «NoReembolsable».</summary>
        public string Modalidad { get; set; }
        /// <summary>«Con cupón de productos» o «Sin cupón».</summary>
        public string Nombre { get; set; }
        public decimal Importe { get; set; }
    }

    /// <summary>NestoAPI#691: una línea de curso del pedido, ya comprobada (evento, modalidad, importe y producto con el que se factura).</summary>
    public class CursoDelPedido
    {
        public LineaPedidoVentaDTO Linea { get; set; }
        public Evento Evento { get; set; }
        public ModalidadInscripcionEvento Modalidad { get; set; }
        /// <summary>El de la modalidad en el evento: el precio de la línea, sin descuentos.</summary>
        public decimal Importe { get; set; }
        /// <summary>ReglasProductoCurso.ParaFacturar: el producto de Nesto sin NR.</summary>
        public string ProductoFacturar { get; set; }
        /// <summary>La inscripción (Reservada) que se ha creado para este curso al guardar el pedido.</summary>
        public int? InscripcionId { get; set; }
    }

    /// <summary>NestoAPI#691: lo que hay que hacer con los cursos del pedido antes de crear nada.</summary>
    public class PreparacionCursos
    {
        /// <summary>El motivo del 400 (de usted), o null.</summary>
        public string Error { get; set; }
        public List<CursoDelPedido> Cursos { get; set; } = new List<CursoDelPedido>();
        /// <summary>La ficha del contacto del pedido (para la cabecera de los pedidos de curso).</summary>
        public Cliente Ficha { get; set; }
        public bool HayCursos => Cursos.Any();

        internal static PreparacionCursos ConError(string error) => new PreparacionCursos { Error = error };
    }

    /// <summary>
    /// NestoAPI#691 (decisiones de Carlos del 22/10/26): las reglas de los cursos vendidos desde la plantilla, sin base de datos.
    /// <list type="bullet">
    /// <item>Un curso es una línea con <see cref="LineaPedidoVentaDTO.EventoId"/> (o de grupo CUR). Todos los cursos del pedido van en
    /// UN pedido aparte (serie CV, prepago), una línea por curso con el almacén y la delegación de su evento: nunca se mezclan con
    /// productos (prorrata de IVA).</item>
    /// <item>Su importe NO suma para nada del pedido de productos: portes, Ganavisiones, regalos, ofertas y mínimos, cupones,
    /// reembolso… Los endpoints de cálculo quitan las líneas de curso antes de calcular (<see cref="QuitarLineasCurso"/>).</item>
    /// </list>
    /// </summary>
    public static class ReglasCursosPlantilla
    {
        public const string MOTIVO_CURSOS = "Cursos";
        /// <summary>Lo que se dice como plazas libres de un evento sin límite.</summary>
        public const int PLAZAS_SIN_LIMITE = 999;
        public const int LONGITUD_TEXTO_LINEA = 50;

        public const string NOMBRE_REEMBOLSABLE = "Con cupón de productos";
        public const string NOMBRE_NO_REEMBOLSABLE = "Sin cupón";
        public const string NOMBRE_GRATUITA = "Plaza sin coste";

        public const string MENSAJE_CURSO_EN_PEDIDO_EXISTENTE =
            "Los cursos van en un pedido aparte y no se pueden añadir a un pedido que ya existe. Cree un pedido nuevo (sin ampliar el pendiente), por favor.";
        public const string MENSAJE_CURSO_EN_PRESUPUESTO =
            "Los cursos no pueden ir en un presupuesto: van en un pedido aparte con prepago. Quite el curso del presupuesto, por favor.";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>El nombre que enseñan TNV y Nesto: «Con cupón de productos» y «Sin cupón».</summary>
        public static string NombreModalidad(ModalidadInscripcionEvento modalidad)
        {
            switch (modalidad)
            {
                case ModalidadInscripcionEvento.Reembolsable: return NOMBRE_REEMBOLSABLE;
                case ModalidadInscripcionEvento.NoReembolsable: return NOMBRE_NO_REEMBOLSABLE;
                default: return NOMBRE_GRATUITA;
            }
        }

        /// <summary>La línea es un curso con evento (de la plantilla).</summary>
        public static bool EsLineaConEvento(LineaPedidoVentaDTO linea) =>
            linea != null && (linea.EventoId.HasValue || !string.IsNullOrWhiteSpace(linea.ModalidadEvento));

        /// <summary>
        /// PUT de un pedido que ya existe: un curso NUEVO (con evento) no se le añade; va en un pedido nuevo (CV, prepago). Las
        /// líneas que ya tenía (las de un pedido de curso) sí se pueden guardar. Null si no hay nada que objetar.
        /// </summary>
        public static string MotivoCursoEnPedidoExistente(PedidoVentaDTO pedido) =>
            pedido?.Lineas?.Any(l => l.EsLineaNueva && EsLineaConEvento(l)) == true ? MENSAJE_CURSO_EN_PEDIDO_EXISTENTE : null;

        /// <summary>La línea es un curso: con evento, o de grupo CUR.</summary>
        public static bool EsLineaCurso(LineaPedidoVentaDTO linea) =>
            EsLineaConEvento(linea)
            || string.Equals(linea?.GrupoProducto?.Trim(), Constantes.Productos.GRUPO_CURSOS, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Quita del pedido las líneas de curso (para los cálculos del pedido de productos: portes, modos, ofertas, fecha de entrega,
        /// agencias…). Nesto ya las quita; esto es por si llegan (NestoApp#244). Devuelve cuántas ha quitado.
        /// </summary>
        public static int QuitarLineasCurso(PedidoVentaDTO pedido)
        {
            if (pedido?.Lineas == null)
            {
                return 0;
            }
            List<LineaPedidoVentaDTO> cursos = pedido.Lineas.Where(EsLineaCurso).ToList();
            foreach (LineaPedidoVentaDTO linea in cursos)
            {
                _ = pedido.Lineas.Remove(linea);
            }
            return cursos.Count;
        }

        /// <summary>Las modalidades que se pueden vender en un pedido (la gratuita no: no hay nada que facturar).</summary>
        public static List<ModalidadEventoCursoDTO> Modalidades(Evento evento) =>
            ReglasModalidadEvento.Disponibles(evento)
                .Where(m => m != ModalidadInscripcionEvento.Gratuita)
                .Select(m => new ModalidadEventoCursoDTO
                {
                    Modalidad = m.ToString(),
                    Nombre = NombreModalidad(m),
                    Importe = ReglasModalidadEvento.Importe(evento, m)
                })
                .Where(m => m.Importe > 0)
                .ToList();

        public static int PlazasLibres(Evento evento, int ocupadas) =>
            evento?.Plazas.HasValue == true ? Math.Max(0, evento.Plazas.Value - ocupadas) : PLAZAS_SIN_LIMITE;

        /// <summary>Se puede vender desde la plantilla: activo, de hoy en adelante, con tienda, curso, modalidad de pago y plazas.</summary>
        public static bool EsVendible(Evento evento, int ocupadas, DateTime hoy) =>
            evento != null && evento.Activo && evento.Fecha.Date >= hoy.Date
            && !string.IsNullOrWhiteSpace(evento.ProductoCurso)
            && PermisosEventos.EsAlmacenDeEventos(evento.Almacen)
            && Modalidades(evento).Any()
            && PlazasLibres(evento, ocupadas) > 0;

        /// <summary>¿Es el producto de ese evento? Su ProductoCurso, o su referencia propia con o sin NR (EVENTO00123 / EVENTO00123NR).</summary>
        public static bool EsProductoDelEvento(string producto, Evento evento)
        {
            string limpio = producto?.Trim();
            if (string.IsNullOrEmpty(limpio) || evento == null)
            {
                return false;
            }
            return string.Equals(limpio, evento.ProductoCurso?.Trim(), StringComparison.OrdinalIgnoreCase)
                || ReglasProductoCurso.EsDelEvento(limpio, evento.Id);
        }

        public static EventoCursoDTO ADTO(Evento evento, int ocupadas) => new EventoCursoDTO
        {
            Id = evento.Id,
            Titulo = evento.Titulo?.Trim(),
            Fecha = evento.Fecha.Date,
            HoraInicio = ReglasModalidadEvento.EscribirHora(evento.HoraInicio),
            Almacen = evento.Almacen?.Trim().ToUpperInvariant(),
            Formato = FormatosEvento.De(evento),
            PlazasLibres = PlazasLibres(evento, ocupadas),
            Modalidades = Modalidades(evento)
        };

        /// <summary>«el martes 10 de noviembre a las 10:00, en nuestra tienda de Reina» u «…, online».</summary>
        public static string CuandoYDonde(Evento evento)
        {
            string cuando = "el " + evento.Fecha.ToString("dddd d 'de' MMMM", es);
            string hora = ReglasModalidadEvento.EscribirHora(evento.HoraInicio);
            if (hora != null)
            {
                cuando += " a las " + hora;
            }
            if (FormatosEvento.EsOnline(evento))
            {
                return cuando + ", online";
            }
            string tienda = PermisosEventos.NombreAlmacen(evento.Almacen);
            return tienda == null ? cuando : $"{cuando}, en nuestra tienda de {tienda}";
        }

        internal static string Euros(decimal importe) => importe.ToString("N2", es);

        /// <summary>
        /// Sugerencia 899 (Carlos, 30/10/26): cómo se pagan las inscripciones según la forma de pago del pedido de la plantilla.
        /// TRN → Transferencia y EFC → Efectivo (sin enlace de pago); cualquier otra (TAR, vacía…) → Tarjeta, con su enlace, como
        /// hasta ahora.
        /// </summary>
        public static FormaPagoInscripcionEvento FormaPagoInscripcion(string formaPagoPedido)
        {
            string forma = Limpiar(formaPagoPedido)?.ToUpperInvariant();
            if (forma == Constantes.FormasPago.TRANSFERENCIA)
            {
                return FormaPagoInscripcionEvento.Transferencia;
            }
            return forma == Constantes.FormasPago.EFECTIVO ? FormaPagoInscripcionEvento.Efectivo : FormaPagoInscripcionEvento.Tarjeta;
        }

        /// <summary>El origen de la inscripción: Nesto (usuario de dominio) o NestoApp.</summary>
        public static string Origen(string usuarioPedido) =>
            (usuarioPedido ?? string.Empty).Contains("\\") ? OrigenInscripcionEvento.Nesto.ToString() : OrigenInscripcionEvento.NestoApp.ToString();

        /// <summary>
        /// La cabecera del pedido de los cursos: la del pedido de la plantilla (cliente, contacto, vendedor, ruta, forma de pago,
        /// IVA), con plazo PRE (prepago), periodo normal (un cliente de fin de mes no se queda el curso sin facturar), todo junto y
        /// la serie que diga SerieSegunLineas (CV).
        /// </summary>
        public static CabPedidoVta Cabecera(PedidoVentaDTO pedido, Cliente ficha, int numero, IReadOnlyList<CursoDelPedido> cursos, DateTime hoy)
        {
            string empresa = pedido.empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string contacto = Limpiar(pedido.contacto) ?? Limpiar(ficha?.Contacto);
            return new CabPedidoVta
            {
                Empresa = empresa,
                Número = numero,
                Nº_Cliente = pedido.cliente?.Trim(),
                Contacto = contacto,
                Fecha = hoy,
                Forma_Pago = Limpiar(pedido.formaPago) ?? Constantes.FormasPago.TARJETA,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Primer_Vencimiento = hoy,
                IVA = Limpiar(pedido.iva) ?? Limpiar(ficha?.IVA),
                Vendedor = Limpiar(pedido.vendedor) ?? Limpiar(ficha?.Vendedor),
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Ruta = Limpiar(pedido.ruta) ?? Limpiar(ficha?.Ruta),
                Serie = Constantes.Series.SERIE_POR_DEFECTO,
                CCC = null,
                Origen = empresa,
                ContactoCobro = Limpiar(pedido.contactoCobro) ?? contacto,
                NoComisiona = pedido.noComisiona,
                MantenerJunto = true,
                ServirJunto = true,
                ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                Comentarios = Truncar((cursos.Count == 1 ? "Curso " : "Cursos ") + string.Join(", ", cursos.Select(c =>
                    $"«{c.Evento.Titulo?.Trim()}» del {c.Evento.Fecha:dd/MM/yyyy}, {NombreModalidad(c.Modalidad).ToLowerInvariant()} (inscripción {c.InscripcionId})")) +
                    " (plantilla, NestoAPI#691)", 200),
                Usuario = Truncar(pedido.Usuario, 30)
            };
        }

        /// <summary>
        /// La línea del curso: el producto sin NR, 1 unidad al importe de la modalidad, sin descuentos, en la tienda del evento.
        /// Nace Pendiente (−1): hasta que se paga su inscripción no entra en ningún albarán (al pagarse pasa a En curso y se factura).
        /// </summary>
        public static LineaPedidoVentaDTO Linea(PedidoVentaDTO pedido, CursoDelPedido curso, DateTime hoy)
        {
            string almacen = ReglasFacturacionCurso.AlmacenDelCurso(curso.Evento);
            return new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = curso.ProductoFacturar,
                Cantidad = 1,
                PrecioUnitario = Math.Round(curso.Importe, 2, MidpointRounding.AwayFromZero),
                AplicarDescuento = false,
                DescuentoLinea = 0,
                DescuentoProducto = 0,
                texto = Truncar(curso.Evento.Titulo?.Trim(), LONGITUD_TEXTO_LINEA),
                estado = Constantes.EstadosLineaVenta.PENDIENTE,
                fechaEntrega = hoy,
                almacen = almacen,
                delegacion = almacen,
                formaVenta = Limpiar(curso.Linea?.formaVenta) ?? InterruptoresEventos.FormaVentaCurso,
                usuario = Truncar(pedido.Usuario, 30),
                vistoBueno = true,
                EventoId = curso.Evento.Id,
                ModalidadEvento = curso.Modalidad.ToString()
            };
        }

        internal static string Truncar(string texto, int max) =>
            string.IsNullOrEmpty(texto) ? texto : texto.Length > max ? texto.Substring(0, max) : texto;

        internal static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }

    /// <summary>NestoAPI#691: los eventos de un curso que se pueden vender desde la plantilla (con sus plazas).</summary>
    public interface ICatalogoCursosPlantilla
    {
        /// <summary>GET api/Eventos/PorProducto: los eventos futuros activos de ese curso con plazas libres, el más cercano antes.</summary>
        Task<List<EventoCursoDTO>> EventosDeProducto(string empresa, string producto);
        /// <summary>Los productos (Eventos.ProductoCurso) que se pueden vender hoy: algún evento futuro activo con plazas.</summary>
        Task<HashSet<string>> ProductosVendibles(string empresa);
        /// <summary>Lo mismo con los eventos ya leídos: las plazas ocupadas de cada uno.</summary>
        Task<Dictionary<int, int>> PlazasOcupadas(List<int> eventos);
    }

    public class CatalogoCursosPlantilla : ICatalogoCursosPlantilla
    {
        private readonly NVEntities db;
        private readonly Func<DateTime> reloj;

        public CatalogoCursosPlantilla(NVEntities db, Func<DateTime> reloj = null)
        {
            this.db = db;
            this.reloj = reloj ?? (() => DateTime.Now);
        }

        public async Task<List<EventoCursoDTO>> EventosDeProducto(string empresa, string producto)
        {
            string limpio = producto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return new List<EventoCursoDTO>();
            }
            List<Evento> eventos = (await Futuros(empresa).ConfigureAwait(false))
                .Where(e => ReglasCursosPlantilla.EsProductoDelEvento(limpio, e)).ToList();
            Dictionary<int, int> ocupadas = await PlazasOcupadas(eventos.Select(e => e.Id).ToList()).ConfigureAwait(false);
            DateTime hoy = reloj().Date;
            return eventos
                .Where(e => ReglasCursosPlantilla.EsVendible(e, Ocupadas(ocupadas, e.Id), hoy))
                .OrderBy(e => e.Fecha).ThenBy(e => e.HoraInicio).ThenBy(e => e.Titulo)
                .Select(e => ReglasCursosPlantilla.ADTO(e, Ocupadas(ocupadas, e.Id)))
                .ToList();
        }

        public async Task<HashSet<string>> ProductosVendibles(string empresa)
        {
            List<Evento> eventos = await Futuros(empresa).ConfigureAwait(false);
            Dictionary<int, int> ocupadas = await PlazasOcupadas(eventos.Select(e => e.Id).ToList()).ConfigureAwait(false);
            DateTime hoy = reloj().Date;
            return new HashSet<string>(eventos
                .Where(e => ReglasCursosPlantilla.EsVendible(e, Ocupadas(ocupadas, e.Id), hoy))
                .Select(e => e.ProductoCurso.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<Evento>> Futuros(string empresa)
        {
            string empresaLimpia = ReglasCursosPlantilla.Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            DateTime hoy = reloj().Date;
            return (await db.Eventos.Where(e => e.Empresa == empresaLimpia && e.Activo && e.Fecha >= hoy && e.ProductoCurso != null)
                .ToListAsync().ConfigureAwait(false))
                .Where(e => e.Fecha.Date >= hoy).ToList();
        }

        /// <summary>Como ServicioEventos: las que ocupan plaza («No podré ir» la libera).</summary>
        public async Task<Dictionary<int, int>> PlazasOcupadas(List<int> ids)
        {
            if (ids == null || !ids.Any())
            {
                return new Dictionary<int, int>();
            }
            List<string> estados = ReglasInscripcionEvento.OCUPAN_PLAZA.Select(e => e.ToString()).ToList();
            var filas = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && estados.Contains(i.Estado))
                .Select(i => new { i.EventoId, i.Estado, i.AsistenciaRespuesta })
                .ToListAsync().ConfigureAwait(false);
            return filas
                .Where(f => ReglasInscripcionEvento.OcupaPlaza(new EventoInscripcion { Estado = f.Estado, AsistenciaRespuesta = f.AsistenciaRespuesta }))
                .GroupBy(f => f.EventoId).ToDictionary(g => g.Key, g => g.Count());
        }

        private static int Ocupadas(Dictionary<int, int> ocupadas, int id) => ocupadas.TryGetValue(id, out int n) ? n : 0;

        /// <summary>
        /// NestoAPI#691: los cursos vendibles que coinciden con lo que se busca (por referencia o nombre, o por la lista de
        /// referencias de Lucene), como líneas de la plantilla con <see cref="LineaPlantillaVenta.EsCurso"/>. El precio es el de la
        /// ficha; el de verdad lo fija la modalidad al meterlo.
        /// </summary>
        public async Task<List<LineaPlantillaVenta>> LineasCursosSinFallar(string empresa, string filtro)
        {
            try
            {
                return await LineasCursos(empresa, filtro).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // La búsqueda de productos no se cae por los cursos: salen sin ellos
                ElmahHelper.Log(new Exception($"[Cursos #691] No se han podido buscar los cursos de «{filtro}»: {ex.Message}", ex));
                return new List<LineaPlantillaVenta>();
            }
        }

        public async Task<List<LineaPlantillaVenta>> LineasCursos(string empresa, string filtro, ICollection<string> referencias = null)
        {
            HashSet<string> vendibles = await ProductosVendibles(empresa).ConfigureAwait(false);
            if (!vendibles.Any())
            {
                return new List<LineaPlantillaVenta>();
            }
            string empresaLimpia = ReglasCursosPlantilla.Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<string> lista = vendibles.ToList();
            var productos = await db.Productos
                .Where(p => p.Empresa == empresaLimpia && lista.Contains(p.Número) && p.Grupo == Constantes.Productos.GRUPO_CURSOS)
                .Select(p => new { p.Número, p.Nombre, p.Estado, p.IVA_Repercutido, p.PVP, p.Grupo })
                .ToListAsync().ConfigureAwait(false);
            string texto = filtro?.Trim();
            HashSet<string> refs = referencias == null ? null : new HashSet<string>(referencias.Select(r => r?.Trim()), StringComparer.OrdinalIgnoreCase);
            return productos
                .Where(p => vendibles.Contains(p.Número?.Trim() ?? string.Empty))
                .Where(p => refs != null
                    ? refs.Contains(p.Número.Trim())
                    : !string.IsNullOrEmpty(texto) && ((p.Número?.IndexOf(texto, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                        || (p.Nombre?.IndexOf(texto, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0))
                .Select(p => new LineaPlantillaVenta
                {
                    producto = p.Número.Trim(),
                    texto = p.Nombre?.Trim(),
                    estado = p.Estado,
                    grupo = p.Grupo?.Trim(),
                    subGrupo = string.Empty,
                    familia = string.Empty,
                    codigoBarras = string.Empty,
                    fechaUltimaVenta = DateTime.MinValue,
                    aplicarDescuento = false,
                    iva = p.IVA_Repercutido,
                    precio = p.PVP ?? 0,
                    EsCurso = true
                })
                .ToList();
        }
    }

    /// <summary>
    /// NestoAPI#691: crea el pedido de los cursos (CV, prepago), una línea por curso, y liga cada inscripción a SU línea en la MISMA
    /// transacción. Cada curso llega con su <see cref="CursoDelPedido.InscripcionId"/>.
    /// </summary>
    public interface ICreadorPedidoCursoPlantilla
    {
        Task<int> Crear(PedidoVentaDTO pedido, Cliente ficha, IReadOnlyList<CursoDelPedido> cursos);
    }

    /// <summary>
    /// NestoAPI#691: el correo de usted a la clienta con sus cursos y cómo pagarlos: el enlace de pago (uno, por el total) con
    /// tarjeta; los datos de la transferencia (sugerencia 899); o «páguela en su tienda» en efectivo. Nunca lanza.
    /// </summary>
    public interface IAvisadorCursoPlantilla
    {
        Task<bool> Avisar(PedidoVentaDTO pedido, IReadOnlyList<CursoDelPedido> cursos, IReadOnlyList<InscripcionEventoDTO> inscripciones,
            string urlPago, int pedidoCursos, FormaPagoInscripcionEvento formaPago);
    }

    public interface IServicioCursosPlantilla
    {
        /// <summary>Comprueba los cursos del pedido (sin escribir nada). Con Error, el pedido no se crea (400).</summary>
        Task<PreparacionCursos> Preparar(PedidoVentaDTO pedido);
        /// <summary>
        /// Una inscripción Reservada por curso + UN pedido de cursos (una línea por curso, cada una ligada a su inscripción) + UN
        /// enlace de pago por el total (solo con tarjeta; con transferencia o efectivo, ninguno: NestoAPI#753) + correo a la clienta
        /// adaptado a la forma de pago (salvo que el usuario haya dicho que no: EnviarCorreoCursos = false).
        /// </summary>
        Task<PedidoAdicionalDTO> CrearPedidoCursos(PedidoVentaDTO pedido, Cliente ficha, IReadOnlyList<CursoDelPedido> cursos, IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#691: los cursos del POST api/PedidosVenta. Cada curso (línea con EventoId y ModalidadEvento) se comprueba antes
    /// de crear nada. Al guardar (Carlos, 22/10/26): se inscribe a la clienta en cada uno (Reservada), se crea UN pedido para todos
    /// los cursos (serie CV, plazo PRE; una línea por curso, con el almacén y la delegación de su evento) y cada inscripción queda
    /// ligada a SU línea en EventosInscripcionesCurso (así #612 no crea otro pedido al cobrar), se genera UN enlace de pago por el
    /// total y se le manda por correo. Al pagarse una inscripción se factura su línea en ese momento, sin esperar al evento.
    /// <para>Un producto CUR sin EventoId junto con productos (o con otros cursos con evento) es un 400: no se sabe a qué evento
    /// va y no puede ir mezclado. Un pedido que solo lleva cursos sin evento sigue como hasta ahora (SerieSegunLineas → CV), igual
    /// que los de canales externos: no se rompen los flujos de las tiendas, de #612 ni de CanalesExternos.</para>
    /// </summary>
    public class ServicioCursosPlantilla : IServicioCursosPlantilla
    {
        private readonly NVEntities db;
        private readonly Func<DateTime> reloj;
        private readonly ICatalogoCursosPlantilla catalogo;
        private readonly Func<IServicioInscripcionesEventos> inscripciones;
        private readonly ICreadorPedidoCursoPlantilla creador;
        private readonly IAvisadorCursoPlantilla avisador;

        public ServicioCursosPlantilla(NVEntities db)
            : this(db, null, null, () => new ServicioInscripcionesEventos(db), new CreadorPedidoCursoPlantilla(db), new AvisadorCursoPlantilla(db))
        {
        }

        internal ServicioCursosPlantilla(NVEntities db, Func<DateTime> reloj, ICatalogoCursosPlantilla catalogo,
            Func<IServicioInscripcionesEventos> inscripciones, ICreadorPedidoCursoPlantilla creador, IAvisadorCursoPlantilla avisador)
        {
            this.db = db;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.catalogo = catalogo ?? new CatalogoCursosPlantilla(db, this.reloj);
            this.inscripciones = inscripciones;
            this.creador = creador;
            this.avisador = avisador;
        }

        public async Task<PreparacionCursos> Preparar(PedidoVentaDTO pedido)
        {
            if (pedido?.Lineas == null || !pedido.Lineas.Any())
            {
                return new PreparacionCursos();
            }
            string empresa = ReglasCursosPlantilla.Limpiar(pedido.empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<LineaPedidoVentaDTO> conEvento = pedido.Lineas.Where(ReglasCursosPlantilla.EsLineaConEvento).ToList();
            bool canalExterno = pedido.Lineas.Any(l => Constantes.FormasVenta.EsCanalExterno(l.formaVenta));

            // Un CUR sin evento no puede ir con productos ni con cursos de la plantilla (no se sabe su evento y no se mezclan series)
            if (!canalExterno)
            {
                List<LineaPedidoVentaDTO> productosSinEvento = pedido.Lineas
                    .Where(l => !ReglasCursosPlantilla.EsLineaConEvento(l) && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                        && !string.IsNullOrWhiteSpace(l.Producto))
                    .ToList();
                if (productosSinEvento.Any())
                {
                    Dictionary<string, string> grupos = await LeerGrupos(empresa, productosSinEvento.Select(l => l.Producto.Trim())).ConfigureAwait(false);
                    List<LineaPedidoVentaDTO> curSinEvento = productosSinEvento
                        .Where(l => grupos.TryGetValue(l.Producto.Trim(), out string g) && g == Constantes.Productos.GRUPO_CURSOS).ToList();
                    if (curSinEvento.Any() && (conEvento.Any() || productosSinEvento.Count > curSinEvento.Count))
                    {
                        return PreparacionCursos.ConError(
                            $"El curso {curSinEvento[0].Producto.Trim()} va en un pedido aparte: elija el evento y la modalidad del curso " +
                            "(o haga un pedido solo con cursos), por favor.");
                    }
                }
            }

            if (!conEvento.Any())
            {
                return new PreparacionCursos();
            }
            if (pedido.EsPresupuesto)
            {
                return PreparacionCursos.ConError(ReglasCursosPlantilla.MENSAJE_CURSO_EN_PRESUPUESTO);
            }

            string cliente = ReglasCursosPlantilla.Limpiar(pedido.cliente);
            string contacto = ReglasCursosPlantilla.Limpiar(pedido.contacto);
            Cliente ficha = (await db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault(c => ReglasCursosPlantilla.Limpiar(c.Contacto) == contacto);
            if (ficha == null)
            {
                return PreparacionCursos.ConError($"No existe el cliente {cliente}/{contacto}.");
            }
            if (ReglasCursosPlantilla.Limpiar(pedido.iva) == null && ReglasCursosPlantilla.Limpiar(ficha.IVA) == null)
            {
                return PreparacionCursos.ConError($"El cliente {cliente}/{contacto} no tiene IVA en la ficha: no se le puede facturar el curso.");
            }

            List<int> ids = conEvento.Where(l => l.EventoId.HasValue).Select(l => l.EventoId.Value).Distinct().ToList();
            Dictionary<int, Evento> eventos = (await db.Eventos.Where(e => ids.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Id);
            Dictionary<int, int> ocupadas = await catalogo.PlazasOcupadas(ids).ConfigureAwait(false);
            List<EventoInscripcion> delCliente = (await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId) && i.Cliente == cliente).ToListAsync().ConfigureAwait(false))
                .Where(i => ReglasCursosPlantilla.Limpiar(i.Contacto) == contacto
                    && ReglasInscripcionEvento.OCUPAN_PLAZA.Contains(ReglasInscripcionEvento.Estado(i.Estado) ?? EstadoInscripcionEvento.Anulada))
                .ToList();

            DateTime hoy = reloj().Date;
            var preparacion = new PreparacionCursos { Ficha = ficha };
            foreach (LineaPedidoVentaDTO linea in conEvento)
            {
                string producto = linea.Producto?.Trim();
                if (!linea.EventoId.HasValue)
                {
                    return PreparacionCursos.ConError($"Falta elegir el evento del curso {producto}. Quítelo y vuelva a añadirlo, por favor.");
                }
                ModalidadInscripcionEvento? modalidad = ReglasModalidadEvento.Interpretar(linea.ModalidadEvento);
                if (!modalidad.HasValue)
                {
                    return PreparacionCursos.ConError($"Falta elegir la modalidad del curso {producto} (con cupón de productos o sin cupón), por favor.");
                }
                if (!eventos.TryGetValue(linea.EventoId.Value, out Evento evento))
                {
                    return PreparacionCursos.ConError($"No existe el evento {linea.EventoId.Value} del curso {producto}.");
                }
                string titulo = evento.Titulo?.Trim();
                if (preparacion.Cursos.Any(c => c.Evento.Id == evento.Id))
                {
                    return PreparacionCursos.ConError($"El curso «{titulo}» está dos veces en el pedido: cada clienta ocupa una plaza.");
                }
                if (linea.Cantidad != 1)
                {
                    return PreparacionCursos.ConError($"Cada curso es una plaza: ponga 1 unidad en «{titulo}», por favor.");
                }
                if (ReglasCursosPlantilla.Limpiar(evento.Empresa) != empresa)
                {
                    return PreparacionCursos.ConError($"El evento «{titulo}» no es de la empresa {empresa}.");
                }
                if (!evento.Activo)
                {
                    return PreparacionCursos.ConError($"El evento «{titulo}» no está activo: no admite inscripciones.");
                }
                if (evento.Fecha.Date < hoy)
                {
                    return PreparacionCursos.ConError($"El evento «{titulo}» ya ha pasado.");
                }
                if (!ReglasCursosPlantilla.EsProductoDelEvento(producto, evento))
                {
                    return PreparacionCursos.ConError($"El producto {producto} no es el curso del evento «{titulo}». Quítelo y vuelva a añadirlo, por favor.");
                }
                if (!PermisosEventos.EsAlmacenDeEventos(evento.Almacen))
                {
                    return PreparacionCursos.ConError(ReglasFacturacionCurso.MotivoSinTienda(evento));
                }
                if (modalidad.Value == ModalidadInscripcionEvento.Gratuita)
                {
                    return PreparacionCursos.ConError($"«{titulo}» es gratuito y no va en un pedido: inscriba a la clienta desde el evento, por favor.");
                }
                ModalidadEventoCursoDTO vendible = ReglasCursosPlantilla.Modalidades(evento).FirstOrDefault(m => m.Modalidad == modalidad.Value.ToString());
                if (vendible == null)
                {
                    return PreparacionCursos.ConError($"«{titulo}» no tiene la modalidad «{ReglasCursosPlantilla.NombreModalidad(modalidad.Value).ToLowerInvariant()}». " +
                        "Elija otra, por favor.");
                }
                if (delCliente.Any(i => i.EventoId == evento.Id))
                {
                    return PreparacionCursos.ConError($"El cliente {cliente}/{contacto} ya está inscrito en «{titulo}».");
                }
                int ocupadasEvento = ocupadas.TryGetValue(evento.Id, out int n) ? n : 0;
                if (ReglasCursosPlantilla.PlazasLibres(evento, ocupadasEvento) <= 0)
                {
                    return PreparacionCursos.ConError($"No quedan plazas en «{titulo}».");
                }
                string paraFacturar = ReglasProductoCurso.ParaFacturar(evento, null);
                if (ReglasProductoCurso.EsDelEvento(paraFacturar, evento.Id))
                {
                    string sinNr = ReglasProductoCurso.Referencia(evento.Id, ModalidadInscripcionEvento.Reembolsable);
                    if (sinNr != paraFacturar)
                    {
                        Dictionary<string, string> existe = await LeerGrupos(empresa, new[] { sinNr }).ConfigureAwait(false);
                        paraFacturar = ReglasProductoCurso.ParaFacturar(evento, p => existe.ContainsKey(p));
                    }
                }
                preparacion.Cursos.Add(new CursoDelPedido
                {
                    Linea = linea,
                    Evento = evento,
                    Modalidad = modalidad.Value,
                    Importe = vendible.Importe,
                    ProductoFacturar = paraFacturar
                });
            }
            return preparacion;
        }

        private async Task<Dictionary<string, string>> LeerGrupos(string empresa, IEnumerable<string> productos)
        {
            List<string> lista = productos.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            if (!lista.Any())
            {
                return new Dictionary<string, string>();
            }
            var filas = await db.Productos.Where(p => p.Empresa == empresa && lista.Contains(p.Número))
                .Select(p => new { p.Número, p.Grupo }).ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(p => p.Número.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Grupo?.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public async Task<PedidoAdicionalDTO> CrearPedidoCursos(PedidoVentaDTO pedido, Cliente ficha, IReadOnlyList<CursoDelPedido> cursos, IPrincipal usuario)
        {
            if (cursos == null || !cursos.Any())
            {
                throw new ArgumentException("No hay cursos que crear.", nameof(cursos));
            }
            IServicioInscripcionesEventos servicio = inscripciones();
            // Sugerencia 899: la forma de pago del pedido manda. Con transferencia o efectivo no hay enlace de tarjeta.
            FormaPagoInscripcionEvento formaPago = ReglasCursosPlantilla.FormaPagoInscripcion(pedido.formaPago);
            bool conEnlace = formaPago == FormaPagoInscripcionEvento.Tarjeta;
            var inscritas = new List<InscripcionEventoDTO>();
            int numero;
            try
            {
                // Una plaza (Reservada) por curso, sin enlace propio: el enlace (con tarjeta) es uno para todo el pedido (abajo)
                foreach (CursoDelPedido curso in cursos)
                {
                    InscripcionEventoDTO inscripcion = await servicio.Inscribir(curso.Evento.Id, new InscribirEventoDTO
                    {
                        Empresa = ReglasCursosPlantilla.Limpiar(pedido.empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Cliente = ReglasCursosPlantilla.Limpiar(pedido.cliente),
                        Contacto = ReglasCursosPlantilla.Limpiar(pedido.contacto),
                        Origen = ReglasCursosPlantilla.Origen(pedido.Usuario),
                        GenerarEnlacePago = false,
                        Correo = null,
                        Modalidad = curso.Modalidad.ToString(),
                        // Tarjeta: la pone el enlace del pedido al generarse (como hasta ahora)
                        FormaPago = conEnlace ? null : formaPago.ToString()
                    }, usuario).ConfigureAwait(false);
                    inscritas.Add(inscripcion);
                    curso.InscripcionId = inscripcion.Id;
                }
                numero = await creador.Crear(pedido, ficha, cursos).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Sin pedido no hay plazas a medias: se anulan las inscripciones ya hechas (liberan la plaza)
                foreach (InscripcionEventoDTO inscrita in inscritas)
                {
                    try
                    {
                        _ = await servicio.Anular(inscrita.Id, usuario).ConfigureAwait(false);
                    }
                    catch (Exception exAnular)
                    {
                        ElmahHelper.Log(new Exception($"[Cursos #691] No se ha podido anular la inscripción {inscrita.Id} tras fallar el pedido de cursos: {exAnular.Message}", exAnular));
                    }
                }
                throw ex is NestoBusinessException
                    ? ex
                    : new NestoBusinessException($"No se ha podido crear el pedido de {Titulos(cursos)}: {ex.Message}", ex);
            }

            // Con tarjeta, UN enlace por el total: al cobrarlo quedan pagadas todas las inscripciones y sale una factura con todas.
            // Con transferencia (la señal de #591 la marca pagada) o efectivo (Marcar pagada en la tienda), ninguno.
            var resultado = new PedidoAdicionalDTO { Numero = numero, Motivo = ReglasCursosPlantilla.MOTIVO_CURSOS };
            string urlPago = null;
            if (conEnlace)
            {
                try
                {
                    urlPago = (await servicio.GenerarEnlacePagoPedidoCursos(numero, inscritas.Select(i => i.Id).ToList(), usuario).ConfigureAwait(false))?.UrlPago;
                }
                catch (Exception ex)
                {
                    // El pedido y las plazas se quedan: se pueden pagar en la tienda (Marcar pagada) o con otro enlace
                    resultado.Aviso = $"No se ha podido generar el enlace de pago del pedido {numero} ({ex.Message}). Las plazas están reservadas: " +
                        "mándele el enlace desde el pedido o márquelas como pagadas al cobrarlas, por favor.";
                    ElmahHelper.Log(new Exception($"[Cursos #691] Pedido de cursos {numero} de {pedido.cliente?.Trim()}: {resultado.Aviso}", ex));
                }
            }

            // Sugerencia 899: Nesto pregunta si se le manda el correo; si no viene la respuesta (NestoApp, TNV), sí, como hasta ahora
            if (pedido.EnviarCorreoCursos != false)
            {
                _ = await avisador.Avisar(pedido, cursos, inscritas, urlPago, numero, formaPago).ConfigureAwait(false);
            }
            return resultado;
        }

        internal static string Titulos(IEnumerable<CursoDelPedido> cursos) =>
            string.Join(", ", cursos.Select(c => $"«{c.Evento.Titulo?.Trim()}»"));
    }

    /// <summary>NestoAPI#691: lo que devuelve el POST cuando el pedido lleva cursos.</summary>
    public class ResultadoPedidoConCursos
    {
        /// <summary>Lo que contestó la creación del pedido de productos si NO se creó (se devuelve tal cual, sin crear cursos).</summary>
        public IHttpActionResult NoCreado { get; set; }
        /// <summary>El cuerpo de la respuesta: el pedido principal (el de productos, o el de cursos si no hay productos).</summary>
        public PedidoVentaDTO Respuesta { get; set; }
    }

    /// <summary>
    /// NestoAPI#691: la separación del pedido de la plantilla. Los productos van al pedido normal (NV) por su cauce de siempre,
    /// SIN las líneas de curso (así nada del pedido de productos las cuenta: portes, Ganavisiones, regalos, ofertas, cupones,
    /// reembolso); todos los cursos, a UN pedido aparte (CV). La respuesta es la de siempre (el pedido de productos) más
    /// PedidosAdicionales con el de cursos; si solo hay cursos, el pedido principal es el de cursos.
    /// </summary>
    public static class OrquestadorPedidoConCursos
    {
        public static async Task<ResultadoPedidoConCursos> Crear(PedidoVentaDTO pedido, PreparacionCursos cursos,
            Func<PedidoVentaDTO, Task<IHttpActionResult>> crearProductos, Func<IReadOnlyList<CursoDelPedido>, Task<PedidoAdicionalDTO>> crearCursos)
        {
            foreach (CursoDelPedido curso in cursos.Cursos)
            {
                _ = pedido.Lineas.Remove(curso.Linea);
            }
            bool hayProductos = pedido.Lineas.Any();
            if (hayProductos)
            {
                IHttpActionResult resultado = await crearProductos(pedido).ConfigureAwait(false);
                if (!(resultado is CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>))
                {
                    return new ResultadoPedidoConCursos { NoCreado = resultado };
                }
            }

            PedidoAdicionalDTO creado = null;
            var avisos = new List<string>();
            try
            {
                creado = await crearCursos(cursos.Cursos).ConfigureAwait(false);
            }
            catch (Exception ex) when (hayProductos)
            {
                // Los productos ya están creados: no se deshacen. Se dice qué cursos faltan y por qué (ELMAH y aviso en la respuesta).
                string fallo = $"No se ha podido crear el pedido de {ServicioCursosPlantilla.Titulos(cursos.Cursos)}: {ex.Message} " +
                    "Inscríbala desde el evento, por favor.";
                avisos.Add(fallo);
                ElmahHelper.Log(new Exception($"[Cursos #691] Pedido de {pedido.cliente?.Trim()}: {fallo}", ex));
            }
            if (!string.IsNullOrWhiteSpace(creado?.Aviso))
            {
                avisos.Add(creado.Aviso);
            }

            if (!hayProductos)
            {
                // Solo cursos: el principal es el de cursos, con sus líneas al precio de su modalidad
                pedido.numero = creado.Numero;
                pedido.serie = Constantes.Series.SERIE_CURSOS;
                pedido.plazosPago = Constantes.PlazosPago.PREPAGO;
                foreach (CursoDelPedido curso in cursos.Cursos)
                {
                    curso.Linea.PrecioUnitario = curso.Importe;
                    curso.Linea.DescuentoLinea = 0;
                    curso.Linea.DescuentoProducto = 0;
                    curso.Linea.AplicarDescuento = false;
                    pedido.Lineas.Add(curso.Linea);
                }
                creado = null;
            }
            pedido.PedidosAdicionales = creado != null ? new List<PedidoAdicionalDTO> { creado } : null;
            pedido.AvisosCursos = avisos.Any() ? avisos : null;
            return new ResultadoPedidoConCursos { Respuesta = pedido };
        }
    }

    /// <summary>
    /// NestoAPI#691: el pedido de los cursos de la plantilla, como el de #612 (CreadorPedidoCurso) pero con todos los cursos: una
    /// línea por curso con el producto sin NR al importe de su modalidad, sin descuentos ni pronto pago, en la tienda de su evento
    /// y Pendiente (−1) hasta que se pague su inscripción; cabecera de la plantilla con plazo PRE. El pedido y las filas de
    /// EventosInscripcionesCurso (Estado EnCurso, con el pedido y SU línea) van en la misma transacción: si algo falla, no queda nada.
    /// </summary>
    public class CreadorPedidoCursoPlantilla : ICreadorPedidoCursoPlantilla
    {
        internal const string SQL_LIGAR = @"
INSERT INTO dbo.EventosInscripcionesCurso (InscripcionId, EmpresaPedido, Pedido, LineaPedido, Estado, Intentos, Usuario, FechaCreacion, FechaModificacion)
VALUES (@p0, @p1, @p2, @p3, 'EnCurso', 0, @p4, GETDATE(), GETDATE())";

        private readonly NVEntities db;
        private readonly Func<IServicioPedidosVenta> servicioPedidos;

        public CreadorPedidoCursoPlantilla(NVEntities db, Func<IServicioPedidosVenta> servicioPedidos = null)
        {
            this.db = db;
            this.servicioPedidos = servicioPedidos ?? (() => new ServicioPedidosVenta());
        }

        public async Task<int> Crear(PedidoVentaDTO pedido, Cliente ficha, IReadOnlyList<CursoDelPedido> cursos)
        {
            if (cursos.Any(c => !c.InscripcionId.HasValue))
            {
                throw new InvalidOperationException("Falta la inscripción de algún curso.");
            }
            string empresa = ReglasCursosPlantilla.Limpiar(pedido.empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            IServicioPedidosVenta servicio = servicioPedidos();
            PlazoPago plazo = servicio.LeerPlazosPago(empresa, Constantes.PlazosPago.PREPAGO)
                ?? throw new InvalidOperationException($"No existen los plazos de pago {Constantes.PlazosPago.PREPAGO}.");
            DateTime hoy = DateTime.Today;
            int numero = db.TomarSiguienteNumeroPedido();

            CabPedidoVta cabecera = ReglasCursosPlantilla.Cabecera(pedido, ficha, numero, cursos, hoy);
            var gestor = new GestorPedidosVenta(servicio);
            var lineas = new List<KeyValuePair<CursoDelPedido, LinPedidoVta>>();
            foreach (CursoDelPedido curso in cursos)
            {
                LinPedidoVta linea = gestor.CrearLineaVta(ReglasCursosPlantilla.Linea(pedido, curso, hoy), numero, empresa, cabecera.IVA, plazo,
                    cabecera.Nº_Cliente, cabecera.Contacto, cabecera.Ruta, cabecera.Vendedor, cabecera.Usuario);
                // El curso vale lo que la modalidad: ni descuento de cliente ni pronto pago (la factura tiene que cuadrar con el cobro)
                linea.DescuentoCliente = 0;
                linea.DescuentoPP = 0;
                linea.Descuento = 0;
                linea.DescuentoProducto = 0;
                linea.Aplicar_Dto = false;
                linea.Fecha_Entrega = hoy;
                gestor.CalcularImportesLinea(linea, cabecera.IVA);
                lineas.Add(new KeyValuePair<CursoDelPedido, LinPedidoVta>(curso, linea));
            }
            cabecera.Serie = SerieSegunLineas.Resolver(cabecera.Serie, empresa, lineas.Select(l => l.Value).ToList());

            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    _ = db.CabPedidoVtas.Add(cabecera);
                    foreach (KeyValuePair<CursoDelPedido, LinPedidoVta> linea in lineas)
                    {
                        _ = db.LinPedidoVtas.Add(linea.Value);
                    }
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    // Cada inscripción, ligada a SU línea (el Nº Orden lo da la base de datos al guardar)
                    foreach (KeyValuePair<CursoDelPedido, LinPedidoVta> linea in lineas)
                    {
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_LIGAR,
                            new SqlParameter("@p0", linea.Key.InscripcionId.Value), new SqlParameter("@p1", empresa), new SqlParameter("@p2", numero),
                            new SqlParameter("@p3", linea.Value.Nº_Orden),
                            new SqlParameter("@p4", RepositorioFacturacionCursos.Usuario(pedido.Usuario))).ConfigureAwait(false);
                    }
                    transaccion.Commit();
                }
                catch
                {
                    transaccion.Rollback();
                    foreach (DbEntityEntry entrada in db.ChangeTracker.Entries()
                        .Where(e => e.Entity == cabecera || lineas.Any(l => l.Value == e.Entity)).ToList())
                    {
                        entrada.State = EntityState.Detached;
                    }
                    _ = Controllers.PedidosVentaController.DevolverNumeroPedidoNoGuardado(db, numero, true);
                    throw;
                }
            }
            return numero;
        }
    }

    /// <summary>
    /// NestoAPI#691: el correo a la clienta (de usted) con sus cursos y cómo pagarlos: con tarjeta, el enlace de pago del pedido;
    /// con transferencia (sugerencia 899), los datos de la cuenta, el concepto con el pedido y el importe; en efectivo, que la
    /// pague en su tienda. Nunca lanza.
    /// </summary>
    public class AvisadorCursoPlantilla : IAvisadorCursoPlantilla
    {
        private readonly NVEntities db;
        private readonly IServicioCorreoElectronico correo;
        private readonly Cobros.ILectorDatosPagoEmpresa lectorDatosPago;

        public AvisadorCursoPlantilla(NVEntities db, IServicioCorreoElectronico correo = null, Cobros.ILectorDatosPagoEmpresa lectorDatosPago = null)
        {
            this.db = db;
            this.correo = correo ?? new ServicioCorreoElectronico();
            this.lectorDatosPago = lectorDatosPago ?? new Cobros.LectorDatosPagoEmpresa(db);
        }

        public async Task<bool> Avisar(PedidoVentaDTO pedido, IReadOnlyList<CursoDelPedido> cursos, IReadOnlyList<InscripcionEventoDTO> inscripciones,
            string urlPago, int pedidoCursos, FormaPagoInscripcionEvento formaPago)
        {
            try
            {
                string empresa = ReglasCursosPlantilla.Limpiar(pedido.empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                string cliente = ReglasCursosPlantilla.Limpiar(pedido.cliente);
                string contacto = ReglasCursosPlantilla.Limpiar(pedido.contacto);
                string destino = await CorreoDelCliente(empresa, cliente, contacto).ConfigureAwait(false);
                if (destino == null)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #691] El cliente {cliente}/{contacto} no tiene correo: no se le ha mandado el correo " +
                        $"de {ServicioCursosPlantilla.Titulos(cursos)} (pedido {pedidoCursos}, {formaPago}). Enlace: {urlPago}"));
                    return false;
                }
                string nombre = inscripciones?.Select(i => i?.Nombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                DateTime? limite = inscripciones?.Where(i => i?.FechaLimitePago != null).Select(i => i.FechaLimitePago).Min();
                DatosTransferenciaPedidoDTO transferencia = formaPago == FormaPagoInscripcionEvento.Transferencia
                    ? DatosTransferencia(empresa, cliente, pedidoCursos, cursos)
                    : null;
                using (MailMessage mail = Correo(cursos, urlPago, limite, pedidoCursos, nombre, destino, formaPago, transferencia))
                {
                    bool enviado = correo.EnviarCorreoSMTP(mail);
                    if (!enviado)
                    {
                        ElmahHelper.Log(new Exception($"[Cursos #691] No se ha podido mandar a {destino} el correo del pedido de cursos {pedidoCursos}."));
                    }
                    return enviado;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cursos #691] No se ha podido mandar el correo del pedido de cursos {pedidoCursos}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// Sugerencia 899: los datos de la transferencia como los de la sugerencia 396 (ServicioDatosTransferenciaPedido): IBAN y
        /// titular de la empresa y concepto «Cliente X - Pedido N». El importe es el de los cursos (lo mismo que cobraría el enlace).
        /// </summary>
        internal DatosTransferenciaPedidoDTO DatosTransferencia(string empresa, string cliente, int pedidoCursos, IReadOnlyList<CursoDelPedido> cursos)
        {
            Models.Cobros.DatosPagoAviso datos = null;
            try
            {
                datos = lectorDatosPago.Leer(empresa);
            }
            catch (Exception ex)
            {
                // Sin IBAN el correo sale igual, pidiendo que nos pidan los datos de la cuenta
                ElmahHelper.Log(new Exception($"[Cursos #691] No se han podido leer los datos bancarios de la empresa {empresa}: {ex.Message}", ex));
            }
            return new DatosTransferenciaPedidoDTO
            {
                Empresa = empresa,
                Pedido = pedidoCursos,
                Cliente = cliente,
                Iban = ReglasCursosPlantilla.Limpiar(datos?.Iban),
                Titular = ReglasCursosPlantilla.Limpiar(datos?.Titular),
                Concepto = ServicioDatosTransferenciaPedido.Concepto(cliente, pedidoCursos),
                Importe = Math.Round(cursos.Sum(c => c.Importe), 2, MidpointRounding.AwayFromZero)
            };
        }

        /// <summary>El de las facturas (cargo 22) del contacto; si no tiene, cualquiera de sus personas de contacto con correo.</summary>
        private async Task<string> CorreoDelCliente(string empresa, string cliente, string contacto)
        {
            var personas = await db.PersonasContactoClientes
                .Where(p => p.Empresa == empresa && p.NºCliente == cliente && p.CorreoElectrónico != null)
                .Select(p => new { p.Contacto, p.Cargo, p.CorreoElectrónico }).ToListAsync().ConfigureAwait(false);
            return personas
                .Where(p => !string.IsNullOrWhiteSpace(p.CorreoElectrónico))
                .OrderBy(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO ? 0 : 1)
                .ThenBy(p => p.Contacto?.Trim() == contacto ? 0 : 1)
                .Select(p => p.CorreoElectrónico.Trim())
                .FirstOrDefault();
        }

        /// <summary>
        /// El correo de usted: cada curso (evento, fecha, tienda u «online», modalidad e importe), el total, el pedido y cómo pagar:
        /// con tarjeta, UN botón con el enlace de pago del pedido; con transferencia, los datos (IBAN, beneficiario, concepto e
        /// importe); en efectivo, que la pague en su tienda.
        /// </summary>
        internal static MailMessage Correo(IReadOnlyList<CursoDelPedido> cursos, string urlPago, DateTime? fechaLimitePago, int pedidoCursos,
            string nombre, string destino, FormaPagoInscripcionEvento formaPago = FormaPagoInscripcionEvento.Tarjeta,
            DatosTransferenciaPedidoDTO transferencia = null)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            bool varios = cursos.Count > 1;
            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px; max-width: 600px;'>");
            cuerpo.Append($"<p>Hola{(string.IsNullOrWhiteSpace(nombre) ? "" : ", " + Html(nombre.Trim()))}:</p>");
            if (varios)
            {
                cuerpo.Append("<p>Le hemos reservado una plaza en estos cursos:</p><ul>");
                foreach (CursoDelPedido curso in cursos)
                {
                    cuerpo.Append($"<li><b>«{Html(curso.Evento.Titulo?.Trim())}»</b>, {Html(ReglasCursosPlantilla.CuandoYDonde(curso.Evento))}. " +
                        $"{Html(ReglasCursosPlantilla.NombreModalidad(curso.Modalidad))}: {ReglasCursosPlantilla.Euros(curso.Importe)} €</li>");
                }
                cuerpo.Append("</ul><ul>");
                cuerpo.Append($"<li>Total: {ReglasCursosPlantilla.Euros(cursos.Sum(c => c.Importe))} €</li>");
            }
            else
            {
                CursoDelPedido curso = cursos[0];
                cuerpo.Append($"<p>Le hemos reservado una plaza en <b>«{Html(curso.Evento.Titulo?.Trim())}»</b>, {Html(ReglasCursosPlantilla.CuandoYDonde(curso.Evento))}.</p>");
                cuerpo.Append("<ul>");
                cuerpo.Append($"<li>Modalidad: {Html(ReglasCursosPlantilla.NombreModalidad(curso.Modalidad))}</li>");
                cuerpo.Append($"<li>Importe: {ReglasCursosPlantilla.Euros(curso.Importe)} €</li>");
            }
            cuerpo.Append($"<li>Pedido: {pedidoCursos}</li>");
            cuerpo.Append("</ul>");
            if (formaPago == FormaPagoInscripcionEvento.Transferencia)
            {
                // Sugerencia 899: transferencia, sin enlace de tarjeta
                cuerpo.Append($"<p>Para confirmar {(varios ? "sus plazas" : "su plaza")}, haga una transferencia con estos datos:</p><ul>");
                if (!string.IsNullOrWhiteSpace(transferencia?.Iban))
                {
                    cuerpo.Append($"<li>IBAN: <b>{Html(transferencia.Iban)}</b></li>");
                }
                if (!string.IsNullOrWhiteSpace(transferencia?.Titular))
                {
                    cuerpo.Append($"<li>Beneficiario: {Html(transferencia.Titular)}</li>");
                }
                string concepto = transferencia?.Concepto ?? $"Pedido {pedidoCursos}";
                decimal importe = transferencia != null && transferencia.Importe > 0 ? transferencia.Importe : cursos.Sum(c => c.Importe);
                cuerpo.Append($"<li>Concepto: <b>{Html(concepto)}</b></li>");
                cuerpo.Append($"<li>Importe: <b>{ReglasCursosPlantilla.Euros(importe)} €</b></li>");
                cuerpo.Append("</ul>");
                if (string.IsNullOrWhiteSpace(transferencia?.Iban))
                {
                    cuerpo.Append("<p>Si no tiene los datos de nuestra cuenta, pídanoslos, por favor.</p>");
                }
                cuerpo.Append("<p>Ponga el concepto tal cual, para que podamos identificar su pago.</p>");
            }
            else if (formaPago == FormaPagoInscripcionEvento.Efectivo)
            {
                // Sugerencia 899: efectivo, en la tienda
                cuerpo.Append(varios
                    ? "<p>Para confirmar sus plazas, páguelas en nuestra tienda.</p>"
                    : "<p>Para confirmar su plaza, páguela en nuestra tienda.</p>");
            }
            else if (!string.IsNullOrWhiteSpace(urlPago))
            {
                cuerpo.Append(varios
                    ? "<p>Para confirmar sus plazas, páguelas todas juntas con este enlace de pago seguro:</p>"
                    : "<p>Para confirmar su plaza, páguela con este enlace de pago seguro:</p>");
                cuerpo.Append($"<p><a href='{Html(urlPago)}' style='display:inline-block;padding:12px 20px;background:#2e7d32;color:#ffffff;" +
                    $"text-decoration:none;border-radius:6px;font-weight:bold;'>{(varios ? "Pagar mis plazas" : "Pagar mi plaza")}</a></p>");
            }
            else
            {
                cuerpo.Append(varios
                    ? "<p>Puede pagarlas en su tienda o pedirnos el enlace de pago.</p>"
                    : "<p>Puede pagarla en su tienda o pedirnos el enlace de pago.</p>");
            }
            if (fechaLimitePago != null)
            {
                cuerpo.Append($"<p>Le guardamos {(varios ? "las plazas" : "la plaza")} hasta el {fechaLimitePago.Value.ToString("dddd d 'de' MMMM", CultureInfo.GetCultureInfo("es-ES"))}.</p>");
            }
            cuerpo.Append("<p>Muchas gracias.</p><p>Nueva Visión</p></body></html>");

            string queSigue = formaPago == FormaPagoInscripcionEvento.Transferencia ? "datos para la transferencia"
                : formaPago == FormaPagoInscripcionEvento.Efectivo ? "reserva"
                : "enlace de pago";
            var mail = new MailMessage
            {
                From = new MailAddress("nuevavision@nuevavision.es", "Nueva Visión"),
                Subject = varios
                    ? $"Sus plazas en {ServicioCursosPlantilla.Titulos(cursos)}: {queSigue}"
                    : $"Su plaza en «{cursos[0].Evento.Titulo?.Trim()}»: {queSigue}",
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(destino);
            return mail;
        }
    }
}
