using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    // Ariadna#12: el mozo no encuentra el producto en el hueco de la parada («No está») y resulta que hay en otro hueco.
    // En vez de darlo por falta, la reserva del picking pasa a ese otro hueco y se coge de allí.
    //
    // Se hace como lo hace el sistema (prdUbicacionPicking / prdDeshacerUbicacionPicking, leídos el 05/10/26):
    //  - Ubicaciones en estado 0: lo que hay libre en un hueco. Estado 3: lo reservado para una línea de un picking
    //    (PedidoVta, NºOrdenVta). Estado 2: pendiente de ubicar (sin hueco).
    //  - Reservar es restar de la fila libre del hueco e insertar una fila en estado 3 con el mismo hueco y la línea.
    //  - Lo que no estaba en el hueco de origen sale de su reserva y queda «pendiente de ubicar», igual que una falta
    //    (SQL_RESERVA_A_PENDIENTE_DE_UBICAR): en los libros estaba ahí y no está, así que alguien tiene que encontrarlo.
    // La línea del pedido no se toca: sigue en el picking con su cantidad.

    /// <summary>La reserva de una línea del picking en el hueco de origen.</summary>
    public class ReservaEnHueco
    {
        /// <summary>Ubicaciones.NºOrden de la reserva (estado 3).</summary>
        public int Ubicacion { get; set; }
        /// <summary>LinPedidoVta.[Nº Orden].</summary>
        public int Linea { get; set; }
        public int Pedido { get; set; }
        public string EmpresaLinea { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Una fila de Ubicaciones en estado 0 (libre) del hueco de destino.</summary>
    public class LibreEnHueco
    {
        public int Ubicacion { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Unidades de una reserva que pasan a cogerse de una fila libre de otro hueco.</summary>
    public class MovimientoDeReserva
    {
        public ReservaEnHueco Reserva { get; set; }
        public LibreEnHueco Libre { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Qué reserva se cambia a qué fila libre. Puro: sin base de datos.</summary>
    public static class PlanificadorCambioHueco
    {
        /// <summary>
        /// Reparte hasta <paramref name="cantidad"/> unidades de las reservas del hueco de origen entre las filas libres
        /// del de destino, en el orden en que vienen unas y otras. Si no hay bastante reservado o libre, hasta donde llegue.
        /// </summary>
        public static List<MovimientoDeReserva> Repartir(IEnumerable<ReservaEnHueco> reservas, IEnumerable<LibreEnHueco> libres, int cantidad)
        {
            var movimientos = new List<MovimientoDeReserva>();
            var quedaLibre = (libres ?? Enumerable.Empty<LibreEnHueco>()).Where(l => l != null && l.Cantidad > 0)
                .Select(l => new { Fila = l, Queda = new[] { l.Cantidad } }).ToList();
            int falta = cantidad;
            foreach (ReservaEnHueco reserva in (reservas ?? Enumerable.Empty<ReservaEnHueco>()).Where(r => r != null && r.Cantidad > 0))
            {
                int deEstaReserva = Math.Min(falta, reserva.Cantidad);
                foreach (var libre in quedaLibre)
                {
                    if (deEstaReserva <= 0)
                    {
                        break;
                    }
                    int trozo = Math.Min(deEstaReserva, libre.Queda[0]);
                    if (trozo <= 0)
                    {
                        continue;
                    }
                    movimientos.Add(new MovimientoDeReserva { Reserva = reserva, Libre = libre.Fila, Cantidad = trozo });
                    libre.Queda[0] -= trozo;
                    deEstaReserva -= trozo;
                    falta -= trozo;
                }
                if (falta <= 0)
                {
                    break;
                }
            }
            return movimientos;
        }
    }

    public interface IRepositorioCambioHueco
    {
        /// <summary>Los otros huecos del almacén del picking donde hay libre de ese producto, en el orden del recorrido.</summary>
        Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int picking, string producto, string huecoOrigen);

        /// <summary>
        /// Pasa la reserva de un hueco a otro, todo en una transacción. Devuelve las unidades cambiadas (0 si en el origen
        /// no hay nada reservado de ese producto para ese picking, o en el destino no hay nada libre).
        /// </summary>
        Task<int> Cambiar(string empresa, int picking, string producto, string huecoOrigen, string huecoDestino, int cantidad, string usuario);
    }

    public class RepositorioCambioHuecoSql : IRepositorioCambioHueco, IDisposable
    {
        // El almacén es el de las líneas del producto en el picking. Solo lo libre con hueco (estado 0): lo pendiente de
        // ubicar (estado 2) no tiene hueco al que mandar al mozo.
        internal const string SQL_ALTERNATIVAS = @"
SELECT RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna) AS Ubicacion,
       RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) AS Codigo,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad
FROM Ubicaciones u
WHERE u.Empresa = @p0 AND u.[Número] = @p2 AND u.Estado = 0 AND u.PedidoVta IS NULL AND u.[AlbaránVta] IS NULL
      AND u.Pasillo IS NOT NULL AND u.Fila IS NOT NULL AND u.Columna IS NOT NULL
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) <> @p3
      AND u.[Almacén] IN (SELECT l.[Almacén] FROM LinPedidoVta l
                          WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1 AND l.Producto = @p2)
GROUP BY u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) > 0
ORDER BY u.Pasillo, u.Columna, u.Fila";

        // El pedido más nuevo primero, como al repartir las faltas (PlanificadorFaltasSalida)
        internal const string SQL_RESERVAS_EN_EL_HUECO = @"
SELECT u.[NºOrden] AS Ubicacion, l.[Nº Orden] AS Linea, l.[Número] AS Pedido, RTRIM(l.Empresa) AS EmpresaLinea,
       RTRIM(l.[Almacén]) AS Almacen, CAST(u.Cantidad AS int) AS Cantidad
FROM LinPedidoVta l WITH (UPDLOCK)
     INNER JOIN Ubicaciones u WITH (UPDLOCK) ON u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1 AND l.Producto = @p2
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) = @p3 AND u.Cantidad > 0
ORDER BY l.[Número] DESC, l.[Nº Orden]";

        internal const string SQL_LIBRES_EN_EL_HUECO = @"
SELECT u.[NºOrden] AS Ubicacion, CAST(u.Cantidad AS int) AS Cantidad
FROM Ubicaciones u WITH (UPDLOCK)
WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.[Número] = @p2 AND u.Estado = 0
      AND u.PedidoVta IS NULL AND u.[AlbaránVta] IS NULL AND u.Cantidad > 0
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) = @p3
ORDER BY u.[FechaCreación], u.[NºOrden]";

        // Como prdUbicacionPicking: la reserva es una fila nueva en estado 3 con el hueco de la libre y la línea del
        // pedido, y la libre se queda con lo que sobra (o desaparece si se queda a cero).
        internal const string SQL_RESERVAR_DE_LA_LIBRE = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Pasillo, Fila, Columna, PedidoVta, Estado, [NºOrdenVta], Usuario)
SELECT @p4, [Almacén], [Número], @p1, Pasillo, Fila, Columna, @p3, 3, @p2, @p5 FROM Ubicaciones WHERE [NºOrden] = @p0;
UPDATE Ubicaciones SET Cantidad = Cantidad - @p1 WHERE [NºOrden] = @p0;
DELETE Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0 AND Estado = 0;";

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioCambioHuecoSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioCambioHuecoSql ConContextoPropio()
        {
            return new RepositorioCambioHuecoSql(new NVEntities()) { contextoPropio = true };
        }

        // Tipados como las columnas (char): con nvarchar SQL Server convertiría cada fila y no usaría el índice
        private static SqlParameter Char(string nombre, int largo, string valor)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Char, largo) { Value = (object)valor ?? DBNull.Value };
        }

        private static SqlParameter Texto(string nombre, string valor)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.VarChar, 30) { Value = (object)valor ?? DBNull.Value };
        }

        public Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int picking, string producto, string huecoOrigen)
        {
            return db.Database.SqlQuery<HuecoAlternativoDTO>(SQL_ALTERNATIVAS,
                Char("@p0", 3, empresa), new SqlParameter("@p1", picking), Char("@p2", 15, producto), Texto("@p3", huecoOrigen ?? string.Empty))
                .ToListAsync();
        }

        public async Task<int> Cambiar(string empresa, int picking, string producto, string huecoOrigen, string huecoDestino, int cantidad, string usuario)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                List<ReservaEnHueco> reservas = await db.Database.SqlQuery<ReservaEnHueco>(SQL_RESERVAS_EN_EL_HUECO,
                    Char("@p0", 3, empresa), new SqlParameter("@p1", picking), Char("@p2", 15, producto), Texto("@p3", huecoOrigen))
                    .ToListAsync().ConfigureAwait(false);
                if (reservas.Count == 0)
                {
                    return 0;
                }
                List<LibreEnHueco> libres = await db.Database.SqlQuery<LibreEnHueco>(SQL_LIBRES_EN_EL_HUECO,
                    Char("@p0", 3, empresa), Char("@p1", 3, reservas[0].Almacen), Char("@p2", 15, producto), Texto("@p3", huecoDestino))
                    .ToListAsync().ConfigureAwait(false);

                List<MovimientoDeReserva> movimientos = PlanificadorCambioHueco.Repartir(reservas, libres, cantidad);
                string quien = string.IsNullOrWhiteSpace(usuario) ? "Ariadna" : (usuario.Length > 30 ? usuario.Substring(0, 30) : usuario);
                foreach (MovimientoDeReserva movimiento in movimientos)
                {
                    // Primero la reserva nueva en el hueco de destino…
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR_DE_LA_LIBRE,
                        new SqlParameter("@p0", movimiento.Libre.Ubicacion), new SqlParameter("@p1", movimiento.Cantidad),
                        new SqlParameter("@p2", movimiento.Reserva.Linea), new SqlParameter("@p3", movimiento.Reserva.Pedido),
                        Char("@p4", 3, movimiento.Reserva.EmpresaLinea), Texto("@p5", quien)).ConfigureAwait(false);
                    // … y lo que no estaba en el de origen, a «pendiente de ubicar», igual que una falta
                    _ = await db.Database.ExecuteSqlCommandAsync(TransaccionSalidaSql.SQL_RESERVA_A_PENDIENTE_DE_UBICAR,
                        movimiento.Reserva.Ubicacion, movimiento.Cantidad, quien).ConfigureAwait(false);
                }
                transaccion.Commit();
                return movimientos.Sum(m => m.Cantidad);
            }
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }

    public enum EstadoCambioHueco
    {
        Cambiado,
        /// <summary>Lo que se pide no vale (falta el producto, un hueco que no es un hueco…).</summary>
        NoValido,
        /// <summary>Ya no hay nada que cambiar: otro mozo lo ha cogido de ahí, o la reserva ya no está en ese hueco.</summary>
        NoSePuede
    }

    public class ResultadoCambioHueco
    {
        public EstadoCambioHueco Estado { get; set; }
        public string Mensaje { get; set; }
        public int Movidas { get; set; }
    }

    public interface IServicioCambioHuecoPicking
    {
        Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int picking, string producto, string hueco);
        Task<ResultadoCambioHueco> Cambiar(string empresa, int picking, CambiarHuecoPickingDTO cambio, string usuario);
    }

    public class ServicioCambioHuecoPicking : IServicioCambioHuecoPicking
    {
        private readonly IRepositorioCambioHueco repositorio;

        public ServicioCambioHuecoPicking(IRepositorioCambioHueco repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int picking, string producto, string hueco)
        {
            if (string.IsNullOrWhiteSpace(producto) || picking <= 0)
            {
                return new List<HuecoAlternativoDTO>();
            }
            return await repositorio.LeerAlternativas(empresa, picking, producto.Trim(), PlanificadorFaltasSalida.NormalizarHueco(hueco))
                .ConfigureAwait(false) ?? new List<HuecoAlternativoDTO>();
        }

        public async Task<ResultadoCambioHueco> Cambiar(string empresa, int picking, CambiarHuecoPickingDTO cambio, string usuario)
        {
            string origen = PlanificadorFaltasSalida.NormalizarHueco(cambio?.HuecoOrigen);
            string destino = PlanificadorFaltasSalida.NormalizarHueco(cambio?.HuecoDestino);
            if (cambio == null || string.IsNullOrWhiteSpace(cambio.Producto))
            {
                return NoValido("Falta el producto.");
            }
            if (origen == null || destino == null)
            {
                return NoValido("Faltan el hueco donde no estaba y el hueco de donde se va a coger.");
            }
            if (origen == destino)
            {
                return NoValido("El hueco de donde se va a coger es el mismo donde no estaba.");
            }
            if (cambio.Cantidad <= 0)
            {
                return NoValido("La cantidad tiene que ser mayor que cero.");
            }

            int movidas = await repositorio.Cambiar(empresa, picking, cambio.Producto.Trim(), origen, destino, cambio.Cantidad, usuario).ConfigureAwait(false);
            if (movidas == 0)
            {
                return new ResultadoCambioHueco
                {
                    Estado = EstadoCambioHueco.NoSePuede,
                    Mensaje = "No se ha cambiado nada: en ese hueco ya no queda libre de ese producto, o la reserva del picking ya no está donde decía. Vuelve a abrir la recogida."
                };
            }
            return new ResultadoCambioHueco
            {
                Estado = EstadoCambioHueco.Cambiado,
                Movidas = movidas,
                Mensaje = movidas < cambio.Cantidad
                    ? $"Solo había {movidas} libres en ese hueco: esas se cogen de allí y el resto sigue donde estaba."
                    : null
            };
        }

        private static ResultadoCambioHueco NoValido(string mensaje)
        {
            return new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoValido, Mensaje = mensaje };
        }
    }
}
