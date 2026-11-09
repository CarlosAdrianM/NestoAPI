using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    // NestoAPI#590 (slice «Quitar picking», Carlos 09/11/26). Hasta ahora solo Nesto viejo quitaba el picking de un pedido: llama a
    // prdDeshacerUbicacionPicking y pone LinPedidoVta.Picking = 0, sin rastro y sin que la PDA que recoge el picking se entere
    // (picking 100033: Pedro siguió leyendo los productos del pedido 930110 y al terminar el servidor lo rechazó).
    // Aquí:
    //  - Las reservas de Ubicaciones se sueltan con el MISMO prdDeshacerUbicacionPicking (línea a línea, @Reubicar = 1: lo
    //    reservado en un hueco vuelve libre a ese hueco; lo reservado de «pendiente de ubicar» vuelve a pendiente), dentro de una
    //    transacción con el Picking = 0 de las líneas y el apunte en Modificaciones. La puerta única de Ubicaciones todavía no
    //    tiene SoltarReservaPicking implementado (PuertaUbicacionesSql: Pendiente), así que se usa el SP de siempre.
    //  - Si el mozo ya ha leído productos de este pedido en el picking, sin Confirmado se devuelven esas lecturas (409) para que
    //    Nesto lo pregunte; con Confirmado se anulan (lecturas en negativo, con el motivo) y la API avisa a la PDA por SignalR.
    //  - No se puede si alguna línea del pedido en ese picking ya está en albarán, si el pedido ya se está empaquetando o si el
    //    picking ya se ha terminado en Ariadna.

    public enum EstadoQuitarPicking
    {
        Quitado,
        /// <summary>El pedido no tiene ninguna línea pendiente con picking.</summary>
        SinPicking,
        /// <summary>Albarán, packing en curso o picking terminado: no se toca nada.</summary>
        NoSePuede,
        /// <summary>El mozo ya ha leído productos de este pedido: hay que confirmar (no se ha tocado nada).</summary>
        HayLecturas,
        SinPermiso
    }

    public class ResultadoQuitarPicking
    {
        public EstadoQuitarPicking Estado { get; set; }
        public ResultadoQuitarPickingDTO Respuesta { get; set; } = new ResultadoQuitarPickingDTO();
    }

    /// <summary>Una línea del pedido con picking (cualquier estado).</summary>
    public class LineaPedidoConPicking
    {
        public int Linea { get; set; }
        public int Pedido { get; set; }
        public int Picking { get; set; }
        public short Estado { get; set; }
        public short TipoLinea { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Lo que se recoge: Cantidad - Recoger.</summary>
        public int Cantidad { get; set; }
        public bool Ficticio { get; set; }
    }

    /// <summary>Lo leído (no faltas) en el picking, neto, por producto, usuario y parada (TotalParada, NestoAPI#713).</summary>
    public class LecturaNetaPicking
    {
        public string Producto { get; set; }
        public string Usuario { get; set; }
        public string Dispositivo { get; set; }
        public int? TotalParada { get; set; }
        public int Unidades { get; set; }
    }

    public interface IRepositorioQuitarPicking
    {
        Task<T> EnTransaccion<T>(Func<ITransaccionQuitarPicking, Task<T>> trabajo);
    }

    public interface ITransaccionQuitarPicking
    {
        /// <summary>Las líneas del pedido con Picking &gt; 0 (cualquier estado), bloqueadas.</summary>
        Task<List<LineaPedidoConPicking>> LeerLineasConPicking(string empresa, int pedido);
        /// <summary>Bloquea las lecturas del picking hasta el final de la transacción. True si el picking ya está terminado en Ariadna.</summary>
        Task<bool> BloquearYMirarSiTerminado(string empresa, int picking);
        /// <summary>True si ya hay algo metido en cajas de este pedido en el packing de ese picking.</summary>
        Task<bool> HayPacking(string empresa, int picking, int pedido);
        Task<List<LecturaNetaPicking>> LeerLecturas(string empresa, int picking);
        /// <summary>Las líneas pendientes (estado 1) que siguen en el picking, de los demás pedidos.</summary>
        Task<List<LineaPedidoConPicking>> LeerLineasDeOtrosPedidos(string empresa, int picking, int pedido);
        /// <summary>prdDeshacerUbicacionPicking de la línea y Picking = 0.</summary>
        Task QuitarPickingDeLaLinea(int linea, string empresa, string usuario);
        Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario);
        Task Auditar(string anterior, string nuevo, string usuario);
    }

    public interface IServicioQuitarPicking
    {
        Task<ResultadoQuitarPicking> Quitar(string empresa, int pedido, bool confirmado, IPrincipal usuario);
    }

    public class ServicioQuitarPicking : IServicioQuitarPicking
    {
        internal const string DISPOSITIVO = "Nesto";

        private readonly IRepositorioQuitarPicking repositorio;

        public ServicioQuitarPicking(IRepositorioQuitarPicking repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <summary>Quien hoy saca (y quitaba en Nesto viejo) el picking: Almacén, Dirección y Administración.</summary>
        public static bool PuedeQuitar(IPrincipal usuario)
        {
            return usuario != null && (EscrituraSoloAlmacenAttribute.PuedeEscribir(usuario) ||
                usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION));
        }

        public async Task<ResultadoQuitarPicking> Quitar(string empresa, int pedido, bool confirmado, IPrincipal usuario)
        {
            if (!PuedeQuitar(usuario))
            {
                return Resultado(EstadoQuitarPicking.SinPermiso, empresa, pedido,
                    "Para quitar el picking hay que ser de Almacén, Administración o Dirección.");
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario?.Identity?.Name);
            return await repositorio.EnTransaccion(tx => Quitar(tx, empresa, pedido, confirmado, quien)).ConfigureAwait(false);
        }

        internal static async Task<ResultadoQuitarPicking> Quitar(ITransaccionQuitarPicking tx, string empresa, int pedido, bool confirmado, string quien)
        {
            List<LineaPedidoConPicking> lineas = await tx.LeerLineasConPicking(empresa, pedido).ConfigureAwait(false) ?? new List<LineaPedidoConPicking>();
            List<LineaPedidoConPicking> pendientes = lineas.Where(l => l.Picking > 0 && (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE ||
                l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)).ToList();
            if (pendientes.Count == 0)
            {
                return Resultado(EstadoQuitarPicking.SinPicking, empresa, pedido, $"El pedido {pedido} no tiene ninguna línea con picking.");
            }
            List<int> pickings = pendientes.Select(l => l.Picking).Distinct().OrderBy(p => p).ToList();

            LineaPedidoConPicking albaranada = lineas.FirstOrDefault(l => pickings.Contains(l.Picking) && l.Estado >= Constantes.EstadosLineaVenta.ALBARAN);
            if (albaranada != null)
            {
                return Resultado(EstadoQuitarPicking.NoSePuede, empresa, pedido,
                    $"Parte del pedido {pedido} del picking {albaranada.Picking} ya está en albarán: no se puede quitar el picking.", pickings);
            }

            var lecturas = new List<LecturaQuitarPickingDTO>();
            var anulaciones = new List<(int Picking, LecturaNetaPicking Lectura, int Unidades)>();
            foreach (int picking in pickings)
            {
                if (await tx.BloquearYMirarSiTerminado(empresa, picking).ConfigureAwait(false))
                {
                    return Resultado(EstadoQuitarPicking.NoSePuede, empresa, pedido,
                        $"El picking {picking} ya está terminado en la PDA: lo del pedido {pedido} ya está cogido. No se puede quitar el picking; hable con el almacén.", pickings);
                }
                if (await tx.HayPacking(empresa, picking, pedido).ConfigureAwait(false))
                {
                    return Resultado(EstadoQuitarPicking.NoSePuede, empresa, pedido,
                        $"El pedido {pedido} ya se está empaquetando (picking {picking}): no se puede quitar el picking; hable con el almacén.", pickings);
                }
                List<LineaPedidoConPicking> delPedido = pendientes.Where(l => l.Picking == picking).ToList();
                List<LineaPedidoConPicking> otros = await tx.LeerLineasDeOtrosPedidos(empresa, picking, pedido).ConfigureAwait(false) ?? new List<LineaPedidoConPicking>();
                List<LecturaNetaPicking> leidas = await tx.LeerLecturas(empresa, picking).ConfigureAwait(false) ?? new List<LecturaNetaPicking>();
                foreach ((LecturaNetaPicking lectura, int unidades) in QueHayQueApartar(delPedido, otros, leidas))
                {
                    anulaciones.Add((picking, lectura, unidades));
                    LineaPedidoConPicking linea = delPedido.First(l => Mismo(l.Producto, lectura.Producto));
                    lecturas.Add(new LecturaQuitarPickingDTO
                    {
                        Picking = picking,
                        Usuario = lectura.Usuario?.Trim(),
                        Dispositivo = lectura.Dispositivo?.Trim(),
                        Producto = lectura.Producto?.Trim(),
                        Nombre = linea.Nombre?.Trim(),
                        Cantidad = unidades
                    });
                }
            }
            lecturas = Agrupar(lecturas);

            if (lecturas.Count > 0 && !confirmado)
            {
                ResultadoQuitarPicking aviso = Resultado(EstadoQuitarPicking.HayLecturas, empresa, pedido, MensajeHayLecturas(pedido, lecturas), pickings);
                aviso.Respuesta.Lecturas = lecturas;
                return aviso;
            }

            foreach (LineaPedidoConPicking linea in pendientes)
            {
                await tx.QuitarPickingDeLaLinea(linea.Linea, empresa, quien).ConfigureAwait(false);
            }
            string motivo = $"Pedido {pedido} sacado del picking por {UsuarioAuditoriaHelper.ParaMostrar(quien)}";
            if (motivo.Length > 100)
            {
                motivo = motivo.Substring(0, 100);
            }
            foreach ((int picking, LecturaNetaPicking lectura, int unidades) in anulaciones)
            {
                _ = await tx.InsertarEscaneo(empresa, new EscaneoAlmacenDTO
                {
                    IdCliente = Guid.NewGuid(),
                    TipoOrigen = CasadorEscaneos.ORIGEN_PICKING,
                    NumeroOrigen = picking,
                    Picking = picking,
                    Pedido = pedido,
                    Producto = lectura.Producto?.Trim(),
                    Fase = CasadorEscaneos.FASE_PICKING,
                    Cantidad = -unidades,
                    Metodo = CasadorEscaneos.METODO_MANUAL,
                    Motivo = motivo,
                    Dispositivo = DISPOSITIVO,
                    FechaEscaneo = DateTime.Now,
                    TotalParada = lectura.TotalParada
                }, string.IsNullOrWhiteSpace(lectura.Usuario) ? quien : lectura.Usuario.Trim()).ConfigureAwait(false);
            }

            string listaPickings = string.Join(", ", pickings);
            string apartar = lecturas.Count > 0 ? "; apartar: " + ListaProductos(lecturas) : string.Empty;
            await tx.Auditar(
                $"Pedido {pedido}: picking {listaPickings} ({pendientes.Count} líneas)",
                $"Pedido {pedido}: sin picking (quitado por {quien} con «Quitar picking» de Nesto){apartar}",
                quien).ConfigureAwait(false);

            string mensaje = $"Quitado el picking {listaPickings} del pedido {pedido} ({pendientes.Count} {(pendientes.Count == 1 ? "línea" : "líneas")}).";
            if (lecturas.Count > 0)
            {
                mensaje += $" Se ha avisado a {Personas(lecturas)} en la PDA para que aparte {ListaProductos(lecturas)} para el pedido {pedido}.";
            }
            ResultadoQuitarPicking hecho = Resultado(EstadoQuitarPicking.Quitado, empresa, pedido, mensaje, pickings);
            hecho.Respuesta.Lineas = pendientes.Count;
            hecho.Respuesta.Lecturas = lecturas;
            return hecho;
        }

        /// <summary>
        /// Lo leído en el picking que sobra al quitar el pedido: por producto, lo leído menos lo que siguen pidiendo los demás pedidos,
        /// como mucho lo que pedía este. Se reparte entre las lecturas (usuario y parada), de la última hacia atrás. Puro.
        /// </summary>
        internal static List<(LecturaNetaPicking Lectura, int Unidades)> QueHayQueApartar(IEnumerable<LineaPedidoConPicking> delPedido,
            IEnumerable<LineaPedidoConPicking> otrosPedidos, IEnumerable<LecturaNetaPicking> lecturas)
        {
            var resultado = new List<(LecturaNetaPicking, int)>();
            List<LecturaNetaPicking> leidas = (lecturas ?? Enumerable.Empty<LecturaNetaPicking>()).Where(l => l != null && l.Unidades > 0).ToList();
            List<LineaPedidoConPicking> siguen = (otrosPedidos ?? Enumerable.Empty<LineaPedidoConPicking>()).Where(SeRecoge).ToList();
            foreach (IGrouping<string, LineaPedidoConPicking> producto in (delPedido ?? Enumerable.Empty<LineaPedidoConPicking>())
                .Where(SeRecoge)
                .GroupBy(l => l.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                int pedia = producto.Sum(l => l.Cantidad);
                int sigue = siguen.Where(l => Mismo(l.Producto, producto.Key)).Sum(l => l.Cantidad);
                List<LecturaNetaPicking> deEste = leidas.Where(l => Mismo(l.Producto, producto.Key)).ToList();
                int sobra = Math.Min(pedia, Math.Max(0, deEste.Sum(l => l.Unidades) - sigue));
                for (int i = deEste.Count - 1; i >= 0 && sobra > 0; i--)
                {
                    int quita = Math.Min(sobra, deEste[i].Unidades);
                    resultado.Add((deEste[i], quita));
                    sobra -= quita;
                }
            }
            return resultado;
        }

        private static bool SeRecoge(LineaPedidoConPicking linea)
        {
            return linea != null && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !linea.Ficticio && linea.Cantidad > 0;
        }

        private static bool Mismo(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Una fila por picking, usuario y producto (las paradas de un mismo producto se suman).</summary>
        private static List<LecturaQuitarPickingDTO> Agrupar(IEnumerable<LecturaQuitarPickingDTO> lecturas)
        {
            return lecturas
                .GroupBy(l => (l.Picking, Usuario: (l.Usuario ?? string.Empty).ToUpperInvariant(), Producto: (l.Producto ?? string.Empty).ToUpperInvariant()))
                .Select(g => new LecturaQuitarPickingDTO
                {
                    Picking = g.Key.Picking,
                    Usuario = g.First().Usuario,
                    Dispositivo = g.Select(l => l.Dispositivo).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)),
                    Producto = g.First().Producto,
                    Nombre = g.First().Nombre,
                    Cantidad = g.Sum(l => l.Cantidad)
                })
                .OrderBy(l => l.Picking).ThenBy(l => l.Usuario, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string MensajeHayLecturas(int pedido, List<LecturaQuitarPickingDTO> lecturas)
        {
            List<string> personas = PersonasDe(lecturas);
            string quien = personas.Count == 0 ? "Un mozo está" : Unir(personas) + (personas.Count == 1 ? " está" : " están");
            string pickings = Unir(lecturas.Select(l => l.Picking).Distinct().Select(p => p.ToString()).ToList());
            return $"{quien} recogiendo el picking {pickings} y ya {(personas.Count > 1 ? "han" : "ha")} leído " +
                $"{ListaProductos(lecturas)} de este pedido. Si lo quita, esas unidades hay que apartarlas para el pedido {pedido} " +
                "(se le avisa en la PDA).";
        }

        private static List<string> PersonasDe(IEnumerable<LecturaQuitarPickingDTO> lecturas)
        {
            return lecturas.Select(l => UsuarioAuditoriaHelper.ParaMostrar(l.Usuario)).Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Personas(List<LecturaQuitarPickingDTO> lecturas)
        {
            List<string> personas = PersonasDe(lecturas);
            return personas.Count == 0 ? "el mozo" : Unir(personas);
        }

        internal static string ListaProductos(IEnumerable<LecturaQuitarPickingDTO> lecturas)
        {
            return Unir(lecturas
                .GroupBy(l => l.Producto ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Sum(l => l.Cantidad)} {(string.IsNullOrWhiteSpace(g.First().Nombre) ? g.Key : g.First().Nombre.Trim())} ({g.Key})")
                .ToList());
        }

        private static string Unir(List<string> partes)
        {
            if (partes.Count <= 1)
            {
                return partes.FirstOrDefault() ?? string.Empty;
            }
            return string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes.Last();
        }

        private static ResultadoQuitarPicking Resultado(EstadoQuitarPicking estado, string empresa, int pedido, string mensaje, List<int> pickings = null)
        {
            return new ResultadoQuitarPicking
            {
                Estado = estado,
                Respuesta = new ResultadoQuitarPickingDTO { Message = mensaje, Empresa = empresa?.Trim(), Pedido = pedido, Pickings = pickings ?? new List<int>() }
            };
        }
    }

    public class RepositorioQuitarPickingSql : IRepositorioQuitarPicking, IDisposable
    {
        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioQuitarPickingSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public static RepositorioQuitarPickingSql ConContextoPropio() => new RepositorioQuitarPickingSql(new NVEntities()) { contextoPropio = true };

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }

        public async Task<T> EnTransaccion<T>(Func<ITransaccionQuitarPicking, Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo(new TransaccionQuitarPickingSql(db)).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (Exception)
                    {
                        // Ya deshecha (el SP hace ROLLBACK si falla)
                    }
                    throw;
                }
            }
        }
    }

    public class TransaccionQuitarPickingSql : ITransaccionQuitarPicking
    {
        internal const string SQL_LINEAS_CON_PICKING = @"
SELECT l.[Nº Orden] AS Linea, l.[Número] AS Pedido, CAST(l.Picking AS int) AS Picking, CAST(l.Estado AS smallint) AS Estado,
       CAST(ISNULL(l.TipoLinea, 0) AS smallint) AS TipoLinea, RTRIM(l.Producto) AS Producto, RTRIM(l.Texto) AS Nombre,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad, CAST(ISNULL(p.Ficticio, 0) AS bit) AS Ficticio
FROM LinPedidoVta l WITH (UPDLOCK)
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND ISNULL(l.Picking, 0) > 0";

        internal const string SQL_LINEAS_DE_OTROS_PEDIDOS = @"
SELECT l.[Nº Orden] AS Linea, l.[Número] AS Pedido, CAST(l.Picking AS int) AS Picking, CAST(l.Estado AS smallint) AS Estado,
       CAST(ISNULL(l.TipoLinea, 0) AS smallint) AS TipoLinea, RTRIM(l.Producto) AS Producto, RTRIM(l.Texto) AS Nombre,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad, CAST(ISNULL(p.Ficticio, 0) AS bit) AS Ficticio
FROM LinPedidoVta l
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.Estado = 1 AND l.[Número] <> @p2";

        internal const string SQL_HAY_PACKING = @"
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1 FROM PreparacionEscaneos e
    WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = @p1 AND e.Fase = 'PACK' AND e.Pedido = @p2
    GROUP BY e.Producto HAVING SUM(e.Cantidad) <> 0) THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_LECTURAS = @"
SELECT RTRIM(e.Producto) AS Producto, RTRIM(e.Usuario) AS Usuario, MAX(RTRIM(e.Dispositivo)) AS Dispositivo, e.TotalParada AS TotalParada,
       CAST(SUM(e.Cantidad) AS int) AS Unidades
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = @p1 AND e.Fase = 'PICK' AND e.Metodo <> 'FALTA'
GROUP BY e.Producto, e.Usuario, e.TotalParada
HAVING SUM(e.Cantidad) > 0
ORDER BY MAX(e.FechaEscaneo)";

        // Lo mismo que hacía Nesto viejo (prdDeshacerUbicacionPicking + Picking = 0), línea a línea y con @Reubicar = 1
        internal const string SQL_QUITAR_PICKING_LINEA = @"
DECLARE @devuelto int;
EXEC @devuelto = prdDeshacerUbicacionPicking @p0, 0, @p1, 1;
IF ISNULL(@devuelto, 0) <> 1 THROW 50000, 'No se pudo deshacer la ubicación del picking', 1;
UPDATE LinPedidoVta SET Picking = 0, Usuario = @p2, [Fecha Modificación] = GETDATE() WHERE [Nº Orden] = @p1 AND Picking > 0;";

        internal const string SQL_AUDITAR = @"
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (N'LinPedidoVta', @p0, @p1, @p2)";

        private readonly NVEntities db;
        private readonly RepositorioPreparacionAlmacen preparacion;

        public TransaccionQuitarPickingSql(NVEntities db)
        {
            this.db = db;
            preparacion = new RepositorioPreparacionAlmacen(db);
        }

        private static SqlParameter Char(string nombre, int largo, string valor)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Char, largo) { Value = (object)valor ?? DBNull.Value };
        }

        public Task<List<LineaPedidoConPicking>> LeerLineasConPicking(string empresa, int pedido)
        {
            return db.Database.SqlQuery<LineaPedidoConPicking>(SQL_LINEAS_CON_PICKING, Char("@p0", 3, empresa), new SqlParameter("@p1", pedido)).ToListAsync();
        }

        public Task<bool> BloquearYMirarSiTerminado(string empresa, int picking)
        {
            // Como una corrección de parada (Ariadna#41): las lecturas del picking y su marca de terminado quedan bloqueadas hasta el final
            return db.Database.SqlQuery<bool>(TransaccionCorreccionSql.SQL_BLOQUEAR_SALIDA, empresa, CasadorEscaneos.ORIGEN_PICKING, picking).FirstAsync();
        }

        public Task<bool> HayPacking(string empresa, int picking, int pedido)
        {
            return db.Database.SqlQuery<bool>(SQL_HAY_PACKING, Char("@p0", 3, empresa), new SqlParameter("@p1", picking), new SqlParameter("@p2", pedido)).FirstAsync();
        }

        public Task<List<LecturaNetaPicking>> LeerLecturas(string empresa, int picking)
        {
            return db.Database.SqlQuery<LecturaNetaPicking>(SQL_LECTURAS, Char("@p0", 3, empresa), new SqlParameter("@p1", picking)).ToListAsync();
        }

        public Task<List<LineaPedidoConPicking>> LeerLineasDeOtrosPedidos(string empresa, int picking, int pedido)
        {
            return db.Database.SqlQuery<LineaPedidoConPicking>(SQL_LINEAS_DE_OTROS_PEDIDOS, Char("@p0", 3, empresa), new SqlParameter("@p1", picking),
                new SqlParameter("@p2", pedido)).ToListAsync();
        }

        public async Task QuitarPickingDeLaLinea(int linea, string empresa, string usuario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_QUITAR_PICKING_LINEA, Char("@p0", 3, empresa), new SqlParameter("@p1", linea),
                new SqlParameter("@p2", System.Data.SqlDbType.VarChar, 30) { Value = Recortar(usuario, 30) }).ConfigureAwait(false);
        }

        public Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario)
        {
            return preparacion.InsertarEscaneo(empresa, escaneo, usuario);
        }

        public async Task Auditar(string anterior, string nuevo, string usuario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_AUDITAR, new SqlParameter("@p0", (object)anterior ?? DBNull.Value),
                new SqlParameter("@p1", (object)nuevo ?? DBNull.Value), new SqlParameter("@p2", (object)usuario ?? DBNull.Value)).ConfigureAwait(false);
        }

        private static object Recortar(string texto, int largo)
        {
            if (texto == null)
            {
                return DBNull.Value;
            }
            string limpio = texto.Trim();
            return limpio.Length > largo ? limpio.Substring(0, largo) : limpio;
        }
    }
}
