using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// «Dónde está lo que falta» (19/11/26, pedido 930942): GET api/PedidosVenta/{empresa}/{pedido}/DondeEstaLoQueFalta. Por cada producto del
    /// pedido que NO está listo en Algete, dónde está y una frase hecha. Sale del motor de #606 (la misma verdad que el pie del correo y la
    /// promesa en riesgo) y, para lo que viene de camino, del seguimiento de la reposición (#772), leído por tanda.
    /// </summary>
    public class DondeEstaLoQueFaltaDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Lo que hay que enseñar arriba: «Todo está en Algete: tiene el picking 100120.», «Falta 1 producto en Algete.»…</summary>
        public string Mensaje { get; set; }
        /// <summary>No falta nada en Algete (o no queda nada pendiente): <see cref="Productos"/> vacía.</summary>
        public bool TodoEnAlgete { get; set; }
        /// <summary>Los pickings de las líneas pendientes (vacía si no tiene).</summary>
        public List<int> Pickings { get; set; } = new List<int>();
        /// <summary>La fecha del motor (la del pie del correo); null = sin fecha.</summary>
        public DateTime? FechaEntregaAgencia { get; set; }
        public List<ProductoQueFaltaDTO> Productos { get; set; } = new List<ProductoQueFaltaDTO>();
    }

    public class ProductoQueFaltaDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Las unidades que NO están en Algete (no las de la línea).</summary>
        public int Unidades { get; set; }
        /// <summary>«43818 MULTIVIT GLOW CREMA RICA (1 ud.): viene en la reposición 81463 de Alcobendas, recogida por Miguel el miércoles 18/11 a las 12:53; pendiente de recibir en Algete.»</summary>
        public string Texto { get; set; }
        /// <summary>Una por sitio (normalmente una).</summary>
        public List<SituacionQueFaltaDTO> Situaciones { get; set; } = new List<SituacionQueFaltaDTO>();
    }

    public class SituacionQueFaltaDTO
    {
        /// <summary>El origen del motor: EnCamino, Tienda, Proveedor, SinStock, Montaje o Kit (<see cref="PartidaFechaEntregaAgenciaDTO"/>).</summary>
        public string Origen { get; set; }
        public int Unidades { get; set; }
        /// <summary>EnCamino: la tienda de la que salió; Tienda: la tienda en la que está (REI, ALC).</summary>
        public string Tienda { get; set; }
        /// <summary>EnCamino: la reposición (NºTraspaso) en la que viene.</summary>
        public int? Traspaso { get; set; }
        public int? PedidoProveedor { get; set; }
        public DateTime? FechaPrevistaProveedor { get; set; }
        /// <summary>Proveedor con la prevista vencida: el día que se supone que llega.</summary>
        public DateTime? LlegadaSupuesta { get; set; }
        /// <summary>Tienda: día y hora a la que suele llegar a Algete la reposición que lo traerá.</summary>
        public DateTime? LlegaAAlgete { get; set; }
        /// <summary>Primer día en que puede salir a la agencia (null = sin fecha).</summary>
        public DateTime? PuedeSalirEl { get; set; }
        public DateTime? FechaOrientativa { get; set; }
        public string Certeza { get; set; }
        public string Kit { get; set; }
        public string ComponenteQueFalta { get; set; }
        public string NombreComponente { get; set; }
        public string OrigenComponente { get; set; }
        public string MotivoSinFecha { get; set; }
        /// <summary>Solo EnCamino: el resumen del seguimiento de la reposición. Null si no se ha podido leer (la frase va sin el detalle).</summary>
        public SeguimientoQueFaltaDTO Seguimiento { get; set; }
        /// <summary>La parte de la frase de esta situación, sin el producto: «viene en la reposición 81463 de Alcobendas, recogida por…».</summary>
        public string Texto { get; set; }
    }

    /// <summary>Lo que importa del seguimiento de una reposición (#772) para quien espera la unidad.</summary>
    public class SeguimientoQueFaltaDTO
    {
        public DateTime? Preparada { get; set; }
        public string PreparadaPor { get; set; }
        /// <summary>Cuándo se la llevó el repartidor (o se le dio al compañero que la lleva).</summary>
        public DateTime? Recogida { get; set; }
        public string RecogidaPor { get; set; }
        /// <summary>NestoAPI#814: el compañero que la lleva (sin furgoneta).</summary>
        public string LaLleva { get; set; }
        /// <summary>«Miguel» si algún bulto está ahora en su furgoneta (varios: «Miguel y Andre»).</summary>
        public string EnFurgonetaDe { get; set; }
        /// <summary>Algún bulto se ha descargado o entregado en Algete y nadie lo ha recibido aún.</summary>
        public bool Descargada { get; set; }
        public DateTime? Recibida { get; set; }
        /// <summary>Todavía no se ha recibido en Algete.</summary>
        public bool PendienteDeRecibir { get; set; }
        public int Bultos { get; set; }
        public int BultosRecogidos { get; set; }
    }

    /// <summary>Una línea del pedido, para el nombre del producto y el picking.</summary>
    public class LineaDondeEstaLoQueFalta
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Estado { get; set; }
        public int TipoLinea { get; set; }
        public int Picking { get; set; }
    }

    public interface ILectorLineasDondeEstaLoQueFalta
    {
        /// <summary>Las líneas del pedido con el nombre del producto (vacía si no tiene).</summary>
        Task<List<LineaDondeEstaLoQueFalta>> LeerLineas(string empresa, int pedido);
    }

    public class LectorLineasDondeEstaLoQueFaltaSql : ILectorLineasDondeEstaLoQueFalta
    {
        internal const string SQL_LINEAS = @"
SELECT RTRIM(l.Producto) AS Producto, RTRIM(ISNULL(p.Nombre, l.Texto)) AS Nombre, CAST(l.Estado AS int) AS Estado,
    CAST(ISNULL(l.TipoLinea, 0) AS int) AS TipoLinea, CAST(ISNULL(l.Picking, 0) AS int) AS Picking
FROM dbo.LinPedidoVta l
LEFT JOIN dbo.Productos p ON p.Empresa = l.Empresa AND p.Número = l.Producto
WHERE l.Empresa = @p0 AND l.Número = @p1";

        private readonly NVEntities db;

        public LectorLineasDondeEstaLoQueFaltaSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<List<LineaDondeEstaLoQueFalta>> LeerLineas(string empresa, int pedido) =>
            db.Database.SqlQuery<LineaDondeEstaLoQueFalta>(SQL_LINEAS, empresa, pedido).ToListAsync();
    }

    public interface IServicioDondeEstaLoQueFalta
    {
        /// <summary>Null si el pedido no existe.</summary>
        Task<DondeEstaLoQueFaltaDTO> Calcular(string empresa, int pedido);
    }

    public class ServicioDondeEstaLoQueFalta : IServicioDondeEstaLoQueFalta
    {
        private readonly IServicioFechaEntregaAgencia motor;
        private readonly ILectorLineasDondeEstaLoQueFalta lineas;
        private readonly Func<string, IEnumerable<int>, Task<Dictionary<int, SeguimientoReposicionDTO>>> seguimientos;
        private readonly Func<DateTime> ahora;

        /// <param name="seguimientos">(empresa, traspasos) → el seguimiento de cada uno, por tanda (<see cref="ServicioSeguimientoReposiciones.SeguimientosTanda"/>).</param>
        public ServicioDondeEstaLoQueFalta(IServicioFechaEntregaAgencia motor, ILectorLineasDondeEstaLoQueFalta lineas,
            Func<string, IEnumerable<int>, Task<Dictionary<int, SeguimientoReposicionDTO>>> seguimientos, Func<DateTime> ahora = null)
        {
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.lineas = lineas ?? throw new ArgumentNullException(nameof(lineas));
            this.seguimientos = seguimientos;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<DondeEstaLoQueFaltaDTO> Calcular(string empresa, int pedido)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            FechaEntregaAgenciaDTO calculo = await motor.CalcularPedido(empresa, pedido).ConfigureAwait(false);
            if (calculo == null)
            {
                return null;
            }
            List<LineaDondeEstaLoQueFalta> leidas = await lineas.LeerLineas(empresa, pedido).ConfigureAwait(false) ?? new List<LineaDondeEstaLoQueFalta>();
            List<int> traspasos = ReglasDondeEstaLoQueFalta.Faltas(calculo)
                .Where(p => p.TraspasoEnCamino.HasValue).Select(p => p.TraspasoEnCamino.Value).Distinct().ToList();
            Dictionary<int, SeguimientoReposicionDTO> leidos = null;
            if (traspasos.Any() && seguimientos != null)
            {
                try
                {
                    leidos = await seguimientos(empresa, traspasos).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Nunca 500 por esto: la frase sale sin el detalle del repartidor.
                    ElmahHelper.Log(new Exception($"[Dónde está lo que falta] Pedido {pedido}: no se ha podido leer el seguimiento de las reposiciones " +
                        $"{string.Join(", ", traspasos)}: {ex.Message}", ex));
                }
            }
            return ReglasDondeEstaLoQueFalta.Componer(empresa, pedido, calculo, leidas, leidos, ahora());
        }
    }

    /// <summary>«Dónde está lo que falta»: las reglas y los textos, puros (de usted, con tildes, fechas con día de la semana y dd/MM, nunca «hoy»).</summary>
    public static class ReglasDondeEstaLoQueFalta
    {
        public const string TODO_EN_ALGETE = "Todo está en Algete";
        public const string NADA_PENDIENTE = "No queda nada pendiente de servir en este pedido.";
        public const string PENDIENTE_RECIBIR = "pendiente de recibir en Algete";
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>Lo listo en Algete (la regla de la promesa en riesgo): ya tiene picking o está libre en Algete. El resto, falta.</summary>
        public static bool EstaListaEnAlgete(PartidaFechaEntregaAgenciaDTO p) =>
            p != null && (p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE || p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING);

        public static List<PartidaFechaEntregaAgenciaDTO> Faltas(FechaEntregaAgenciaDTO calculo) =>
            (calculo?.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => p != null && !EstaListaEnAlgete(p) && !string.IsNullOrWhiteSpace(p.Producto) && p.Unidades != 0)
                .ToList();

        public static DondeEstaLoQueFaltaDTO Componer(string empresa, int pedido, FechaEntregaAgenciaDTO calculo, IList<LineaDondeEstaLoQueFalta> lineas,
            IDictionary<int, SeguimientoReposicionDTO> seguimientos, DateTime hoy)
        {
            List<LineaDondeEstaLoQueFalta> todas = (lineas ?? new List<LineaDondeEstaLoQueFalta>()).Where(l => l != null).ToList();
            List<LineaDondeEstaLoQueFalta> pendientes = todas
                .Where(l => (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                    && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                .ToList();
            var resultado = new DondeEstaLoQueFaltaDTO
            {
                Empresa = empresa,
                Pedido = pedido,
                FechaEntregaAgencia = calculo?.FechaEntregaAgencia,
                Pickings = pendientes.Where(l => l.Picking > 0).Select(l => l.Picking).Distinct().OrderBy(p => p).ToList()
            };
            Dictionary<string, string> nombres = todas
                .Where(l => !string.IsNullOrWhiteSpace(l.Producto) && !string.IsNullOrWhiteSpace(l.Nombre))
                .GroupBy(l => l.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Nombre.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, PartidaFechaEntregaAgenciaDTO> grupo in Faltas(calculo).GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                List<PartidaFechaEntregaAgenciaDTO> partidas = grupo.ToList();
                bool varias = partidas.Count > 1;
                var producto = new ProductoQueFaltaDTO
                {
                    Producto = grupo.Key,
                    Nombre = nombres.TryGetValue(grupo.Key, out string nombre) ? nombre : null,
                    Unidades = partidas.Sum(p => p.Unidades)
                };
                foreach (PartidaFechaEntregaAgenciaDTO p in partidas)
                {
                    SeguimientoQueFaltaDTO seguimiento = null;
                    if (p.TraspasoEnCamino.HasValue && seguimientos != null && seguimientos.TryGetValue(p.TraspasoEnCamino.Value, out SeguimientoReposicionDTO s))
                    {
                        seguimiento = Resumir(s);
                    }
                    SituacionQueFaltaDTO situacion = Situacion(p, seguimiento);
                    situacion.Texto = TextoSituacion(p, seguimiento, varias && p.Unidades > 1, hoy);
                    producto.Situaciones.Add(situacion);
                }
                producto.Texto = TextoProducto(producto, hoy);
                resultado.Productos.Add(producto);
            }

            resultado.TodoEnAlgete = resultado.Productos.Count == 0;
            resultado.Mensaje = Mensaje(resultado, pendientes.Any());
            return resultado;
        }

        /// <summary>«Todo está en Algete: tiene el picking 100120.», «… aún no tiene picking.», «Falta 1 producto en Algete.»…</summary>
        public static string Mensaje(DondeEstaLoQueFaltaDTO resultado, bool hayPendiente)
        {
            int n = resultado?.Productos?.Count ?? 0;
            if (n > 0)
            {
                return n == 1 ? "Falta 1 producto en Algete." : $"Faltan {n} productos en Algete.";
            }
            if (!hayPendiente)
            {
                return NADA_PENDIENTE;
            }
            List<int> pickings = resultado?.Pickings ?? new List<int>();
            if (pickings.Count == 0)
            {
                return TODO_EN_ALGETE + "; aún no tiene picking.";
            }
            return pickings.Count == 1
                ? $"{TODO_EN_ALGETE}: tiene el picking {pickings[0]}."
                : $"{TODO_EN_ALGETE}: tiene los pickings {Enumerar(pickings.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList())}.";
        }

        /// <summary>
        /// «43818 MULTIVIT GLOW CREMA RICA (1 ud.): viene en la reposición 81463 de Alcobendas, recogida por Miguel el miércoles 18/11 a las 12:53;
        /// pendiente de recibir en Algete.» Con varios sitios: «… (3 uds.): 1 ud.: viene en…; 2 uds.: están en Reina…».
        /// </summary>
        public static string TextoProducto(ProductoQueFaltaDTO producto, DateTime hoy)
        {
            string cabeza = $"{Cabeza(producto.Producto, producto.Nombre)} ({Unidades(producto.Unidades)})";
            if (producto.Situaciones.Count == 1)
            {
                return $"{cabeza}: {producto.Situaciones[0].Texto}.";
            }
            return $"{cabeza}: " + string.Join("; ", producto.Situaciones.Select(s => $"{Unidades(s.Unidades)}: {s.Texto}")) + ".";
        }

        /// <summary>La parte de la frase de una partida, sin el producto ni el punto final.</summary>
        /// <param name="plural">El sujeto son varias unidades («2 uds.: están en Reina»).</param>
        public static string TextoSituacion(PartidaFechaEntregaAgenciaDTO p, SeguimientoQueFaltaDTO seguimiento, bool plural, DateTime hoy)
        {
            Func<DateTime, string> dia = d => TextosPromesaRiesgo.DiaCorto(d, hoy);
            switch (p.Origen)
            {
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO:
                    return $"{V(plural, "viene", "vienen")} {CalculadoraFechaEntregaAgencia.DeCamino(p)}{TextoSeguimiento(seguimiento, hoy)}";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA:
                    string tienda = CalculadoraFechaEntregaAgencia.NombreAlmacen(p.Tienda) ?? "una tienda";
                    return $"{V(plural, "está", "están")} en {tienda} y {V(plural, "vendrá", "vendrán")} en la próxima reposición" +
                        (p.LlegaAAlgete.HasValue ? $", que suele llegar a Algete {ElDiaYHora(p.LlegaAAlgete.Value, hoy)}" : string.Empty);
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR:
                    string delPedido = p.PedidoProveedor.HasValue ? $"en el pedido de compra {p.PedidoProveedor.Value}" : "en un pedido de compra";
                    string viene = $"{V(plural, "viene", "vienen")} del proveedor {delPedido}";
                    if (p.LlegadaSupuesta.HasValue && p.FechaPrevistaProveedor.HasValue)
                    {
                        return $"{viene}; la llegada prevista era el {dia(p.FechaPrevistaProveedor.Value)} y aún no ha llegado (suponemos que llega el {dia(p.LlegadaSupuesta.Value)})";
                    }
                    if (p.ConSeguimientoAgencia && p.FechaPrevistaProveedor.HasValue)
                    {
                        return $"{viene}, {TextosCertezaFechaEntrega.LlegadaProveedor(p, dia)}";
                    }
                    return p.FechaPrevistaProveedor.HasValue ? $"{viene}, con llegada prevista el {dia(p.FechaPrevistaProveedor.Value)}" : viene;
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT:
                    return $"se {V(plural, "saca", "sacan")} desmontando el kit {p.Kit?.Trim()} en Algete";
                case PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE:
                    if (string.IsNullOrWhiteSpace(p.ComponenteQueFalta))
                    {
                        return $"{V(plural, "es un pack que se monta", "son packs que se montan")} en Algete con sus componentes, que ya están allí";
                    }
                    string llegada = MontajeKits.LlegadaComponente(p, dia);
                    return $"{V(plural, "es un pack que se monta", "son packs que se montan")} en Algete cuando llegue {MontajeKits.Componente(p)}" +
                        (llegada == null ? ", que aún no tiene fecha de llegada" : $", que {llegada}") +
                        (p.TraspasoEnCamino.HasValue ? TextoSeguimiento(seguimiento, hoy) : string.Empty);
                default:
                    string orientativa = TextosCertezaFechaEntrega.SinFechaOrientativa(p, dia);
                    if (orientativa != null)
                    {
                        return $"sin fecha todavía: {orientativa}";
                    }
                    string motivo = p.MotivoSinFecha?.Trim().TrimEnd('.');
                    return string.IsNullOrEmpty(motivo) ? "no hay stock ni fecha de llegada" : $"sin fecha: {motivo}";
            }
        }

        /// <summary>
        /// Lo que sigue a «viene en la reposición 81463 de Alcobendas»: «, recogida por Miguel el miércoles 18/11 a las 12:53; pendiente de recibir
        /// en Algete». Sin seguimiento (no se ha podido leer): «; pendiente de recibir en Algete».
        /// </summary>
        public static string TextoSeguimiento(SeguimientoQueFaltaDTO s, DateTime hoy)
        {
            if (s == null)
            {
                return "; " + PENDIENTE_RECIBIR;
            }
            if (s.Recibida.HasValue && !s.PendienteDeRecibir)
            {
                return $", recibida en Algete {ElDiaYHora(s.Recibida.Value, hoy)}";
            }
            var partes = new List<string>();
            string principal;
            if (!string.IsNullOrWhiteSpace(s.LaLleva))
            {
                principal = $", la lleva {s.LaLleva.Trim()}" + (s.Recogida.HasValue ? $" desde {ElDiaYHora(s.Recogida.Value, hoy)}" : string.Empty);
            }
            else if (s.Recogida.HasValue)
            {
                principal = $", recogida por {s.RecogidaPor ?? "el repartidor"} {ElDiaYHora(s.Recogida.Value, hoy)}";
                if (s.Bultos > 1 && s.BultosRecogidos > 0 && s.BultosRecogidos < s.Bultos)
                {
                    principal += $" ({s.BultosRecogidos} de {s.Bultos} bultos)";
                }
            }
            else if (s.Preparada.HasValue)
            {
                return $", preparada {ElDiaYHora(s.Preparada.Value, hoy)}; pendiente de que la recoja el repartidor";
            }
            else
            {
                return "; " + PENDIENTE_RECIBIR;
            }
            if (!string.IsNullOrWhiteSpace(s.EnFurgonetaDe)
                && !string.Equals(s.EnFurgonetaDe.Trim(), s.RecogidaPor?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                partes.Add($"está en la furgoneta de {s.EnFurgonetaDe.Trim()}");
            }
            if (s.Descargada)
            {
                partes.Add("descargada en Algete");
            }
            partes.Add(PENDIENTE_RECIBIR);
            return principal + "; " + string.Join("; ", partes);
        }

        /// <summary>El resumen del seguimiento de #772 (eventos del traspaso y estado de cada bulto).</summary>
        public static SeguimientoQueFaltaDTO Resumir(SeguimientoReposicionDTO s)
        {
            if (s == null)
            {
                return null;
            }
            EventoSeguimientoReposicionDTO preparada = s.Eventos?.LastOrDefault(e => e.Tipo == EventoSeguimientoReposicionDTO.PREPARADA);
            EventoSeguimientoReposicionDTO recogida = s.Eventos?.LastOrDefault(e => e.Tipo == EventoSeguimientoReposicionDTO.RECOGIDA);
            EventoSeguimientoReposicionDTO recibida = s.Eventos?.LastOrDefault(e => e.Tipo == EventoSeguimientoReposicionDTO.RECIBIDA);
            List<BultoSeguimientoReposicionDTO> bultos = s.Bultos ?? new List<BultoSeguimientoReposicionDTO>();
            List<EventoSeguimientoReposicionDTO> recogidasBultos = bultos
                .SelectMany(b => b.Eventos ?? new List<EventoSeguimientoReposicionDTO>())
                .Where(e => e.Tipo == TiposMovimientoBulto.RECOGIDO || e.Tipo == TiposMovimientoBulto.CARGADO)
                .OrderBy(e => e.FechaHora)
                .ToList();
            // Sin la marca de la cabecera, la primera lectura del repartidor en la tienda
            EventoSeguimientoReposicionDTO primeraRecogida = recogida ?? recogidasBultos.FirstOrDefault();
            List<string> enFurgoneta = bultos
                .Select(b => (b.Eventos ?? new List<EventoSeguimientoReposicionDTO>()).LastOrDefault())
                .Where(e => e != null && (e.Tipo == TiposMovimientoBulto.RECOGIDO || e.Tipo == TiposMovimientoBulto.CARGADO) && !string.IsNullOrWhiteSpace(e.Usuario))
                .Select(e => e.Usuario.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            bool todosRecibidos = bultos.Any() && bultos.All(b => (b.Eventos ?? new List<EventoSeguimientoReposicionDTO>()).Any(e => e.Tipo == EventoSeguimientoReposicionDTO.RECIBIDO));
            bool recibidaEntera = recibida != null || todosRecibidos;
            return new SeguimientoQueFaltaDTO
            {
                Preparada = preparada?.FechaHora,
                PreparadaPor = preparada?.Usuario,
                Recogida = primeraRecogida?.FechaHora,
                RecogidaPor = primeraRecogida?.Usuario,
                LaLleva = s.LaLleva,
                EnFurgonetaDe = enFurgoneta.Any() ? Enumerar(enFurgoneta) : null,
                Descargada = bultos.Any(b =>
                {
                    EventoSeguimientoReposicionDTO ultimo = (b.Eventos ?? new List<EventoSeguimientoReposicionDTO>()).LastOrDefault();
                    return ultimo != null && (ultimo.Tipo == TiposMovimientoBulto.DESCARGADO || ultimo.Tipo == TiposMovimientoBulto.ENTREGADO);
                }),
                Recibida = recibida?.FechaHora ?? (todosRecibidos
                    ? bultos.SelectMany(b => b.Eventos).Where(e => e.Tipo == EventoSeguimientoReposicionDTO.RECIBIDO).Max(e => (DateTime?)e.FechaHora)
                    : null),
                PendienteDeRecibir = !recibidaEntera,
                Bultos = bultos.Count == 0 ? 0 : Math.Max(bultos.Count, bultos.Max(b => b.Total)),
                BultosRecogidos = bultos.Count(b => (b.Eventos ?? new List<EventoSeguimientoReposicionDTO>())
                    .Any(e => e.Tipo == TiposMovimientoBulto.RECOGIDO || e.Tipo == TiposMovimientoBulto.CARGADO))
            };
        }

        private static SituacionQueFaltaDTO Situacion(PartidaFechaEntregaAgenciaDTO p, SeguimientoQueFaltaDTO seguimiento) => new SituacionQueFaltaDTO
        {
            Origen = p.Origen,
            Unidades = p.Unidades,
            Tienda = p.Origen == PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO ? p.TiendaEnCamino?.Trim() : p.Tienda?.Trim(),
            Traspaso = p.TraspasoEnCamino,
            PedidoProveedor = p.PedidoProveedor,
            FechaPrevistaProveedor = p.FechaPrevistaProveedor,
            LlegadaSupuesta = p.LlegadaSupuesta,
            LlegaAAlgete = p.LlegaAAlgete,
            PuedeSalirEl = p.PuedeSalirEl,
            FechaOrientativa = p.FechaOrientativa,
            Certeza = p.Certeza,
            Kit = p.Kit?.Trim(),
            ComponenteQueFalta = p.ComponenteQueFalta?.Trim(),
            NombreComponente = p.NombreComponente?.Trim(),
            OrigenComponente = p.OrigenComponente,
            MotivoSinFecha = p.MotivoSinFecha,
            Seguimiento = seguimiento
        };

        /// <summary>«el miércoles 18/11 a las 12:53» (sin hora si es medianoche).</summary>
        public static string ElDiaYHora(DateTime momento, DateTime hoy)
        {
            string elDia = TextosPromesaRiesgo.ElDia(momento.Date, hoy);
            return momento.TimeOfDay == TimeSpan.Zero ? elDia : $"{elDia} a las {momento.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        }

        private static string Cabeza(string producto, string nombre) =>
            string.IsNullOrWhiteSpace(nombre) ? producto?.Trim() : $"{producto?.Trim()} {nombre.Trim()}";

        private static string Unidades(int n) => n == 1 ? "1 ud." : $"{n} uds.";

        private static string V(bool plural, string singular, string plurales) => plural ? plurales : singular;

        private static string Enumerar(IList<string> partes)
        {
            if (partes.Count <= 1)
            {
                return string.Join(string.Empty, partes);
            }
            return string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes.Last();
        }
    }
}
