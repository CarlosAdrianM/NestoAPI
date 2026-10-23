using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Ubicaciones;
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
    // Ariadna#12 (salida de una reposición desde Algete): el mozo da «No está» en un hueco y hay en otro. Igual que en el
    // picking (CambioHuecoPicking), la reserva pasa de hueco en vez de darlo por falta; aquí la reserva es la de la
    // reposición (Ubicaciones en estado 4 con NºTraspasoRepo, NestoAPI#651) y el cambio lo escribe la puerta única de
    // Ubicaciones (CambiarHuecoReservaReposicion). Diferencias con el picking:
    //  - Todo o nada: si en el destino no hay libre toda la cantidad, 409 y no se toca nada (en el picking se cambia lo que haya).
    //  - Solo mientras la salida esté viva: ni terminada en Ariadna (PreparacionSalidasTerminadas) ni contabilizada.
    // Lo leído en la PDA sigue valiendo sin hacer nada: las lecturas son del producto, no del hueco, y el núcleo las reparte
    // entre sus paradas (CasadorEscaneos.RepartirLoResuelto). Lo reservado para el traspaso no cambia (solo de hueco), así que
    // lo esperado tampoco.

    public interface IRepositorioCambioHuecoReposicion
    {
        /// <summary>Los otros huecos del almacén de origen donde hay libre del producto (sin ordenar: lo ordena el servicio).</summary>
        Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int traspaso, string producto, string huecoOrigen);

        /// <summary>El almacén de origen de la reposición (el de los huecos). Null si no tiene salida pendiente.</summary>
        Task<string> LeerAlmacenOrigen(string empresa, int traspaso);

        /// <summary>
        /// En una transacción: comprueba que la salida sigue viva y cambia la reserva por la puerta de Ubicaciones. Con
        /// <paramref name="ensayo"/>, lo mismo, deshecho siempre y con las filas antes y después.
        /// </summary>
        Task<ResultadoCambioHueco> Cambiar(string empresa, int traspaso, string producto, string huecoOrigen, string huecoDestino, int cantidad,
            string usuario, bool ensayo);
    }

    public class RepositorioCambioHuecoReposicionSql : IRepositorioCambioHuecoReposicion, IDisposable
    {
        // Como SQL_ALTERNATIVAS del picking: lo libre con hueco (estado 0, sin pedido, albarán ni reserva) del producto, en el
        // almacén de la salida del traspaso (y solo si el producto está en ella), menos el hueco donde no estaba.
        internal const string SQL_ALTERNATIVAS = @"
SELECT RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna) AS Ubicacion,
       RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) AS Codigo,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad
FROM Ubicaciones u
WHERE u.Empresa = @p0 AND u.[Número] = @p2 AND u.Estado = 0 AND u.PedidoVta IS NULL AND u.[AlbaránVta] IS NULL
      AND u.[NºOrdenVta] IS NULL AND u.[NºOrdenRepo] IS NULL
      AND u.Pasillo IS NOT NULL AND u.Fila IS NOT NULL AND u.Columna IS NOT NULL
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) <> @p3
      AND u.[Almacén] IN (SELECT s.[Almacén] FROM PreExtrProducto s
                               INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
                          WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0
                                AND s.[Número] = @p2)
GROUP BY u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) > 0";

        private readonly NVEntities db;
        private readonly Func<NVEntities, IPuertaUbicaciones> puerta;
        private bool contextoPropio;

        /// <param name="puerta">Para las pruebas; si no, la puerta SQL sobre el mismo contexto (y la misma transacción).</param>
        public RepositorioCambioHuecoReposicionSql(NVEntities db, Func<NVEntities, IPuertaUbicaciones> puerta = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.puerta = puerta ?? (contexto => new PuertaUbicacionesSql(contexto));
        }

        public static RepositorioCambioHuecoReposicionSql ConContextoPropio()
        {
            return new RepositorioCambioHuecoReposicionSql(new NVEntities()) { contextoPropio = true };
        }

        private static SqlParameter Char(string nombre, int largo, string valor)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Char, largo) { Value = (object)valor ?? DBNull.Value };
        }

        public Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int traspaso, string producto, string huecoOrigen)
        {
            return db.Database.SqlQuery<HuecoAlternativoDTO>(SQL_ALTERNATIVAS,
                Char("@p0", 3, empresa), new SqlParameter("@p1", traspaso), Char("@p2", 15, producto),
                new SqlParameter("@p3", System.Data.SqlDbType.VarChar, 9) { Value = huecoOrigen ?? string.Empty })
                .ToListAsync();
        }

        public async Task<string> LeerAlmacenOrigen(string empresa, int traspaso)
        {
            return (await db.Database.SqlQuery<string>(RepositorioPreparacionAlmacen.SQL_ORIGEN_REPOSICION, Char("@p0", 3, empresa), new SqlParameter("@p1", traspaso))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<ResultadoCambioHueco> Cambiar(string empresa, int traspaso, string producto, string huecoOrigen, string huecoDestino, int cantidad,
            string usuario, bool ensayo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                var resultado = new ResultadoCambioHueco { Ensayo = ensayo };
                try
                {
                    // Bloquea la marca de terminada hasta el final: un «Terminar» a la vez espera a que esto acabe (y al revés)
                    if (await db.Database.SqlQuery<bool>(TransaccionSalidaSql.SQL_ESTA_TERMINADA, empresa, CasadorEscaneos.ORIGEN_REPOSICION, traspaso)
                        .FirstAsync().ConfigureAwait(false))
                    {
                        return NoSePuede(resultado, $"La reposición {traspaso} ya está terminada: ya no se puede cambiar de hueco.");
                    }
                    DiarioSalidaReposicion diario = await db.Database.SqlQuery<DiarioSalidaReposicion>(TransaccionSalidaSql.SQL_DIARIO_SALIDA, empresa, traspaso)
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    if (diario == null)
                    {
                        return NoSePuede(resultado, $"La reposición {traspaso} ya no tiene salida pendiente (¿ya ha salido?): ya no se puede cambiar de hueco.");
                    }

                    DateTime desde = ensayo ? await FotoCambioHueco.Ahora(db).ConfigureAwait(false) : DateTime.MinValue;
                    if (ensayo)
                    {
                        resultado.FilasAntes = await FotoCambioHueco.Leer(db, diario.Almacen, producto, huecoOrigen, huecoDestino, desde).ConfigureAwait(false);
                    }
                    ResumenUbicaciones resumen = await puerta(db).CambiarHuecoReservaReposicion(empresa, diario.Almacen, traspaso, producto,
                        HuecoUbicacion.DeCodigo(huecoOrigen), HuecoUbicacion.DeCodigo(huecoDestino), cantidad, usuario).ConfigureAwait(false);
                    resultado.Movidas = resumen.Movimientos.Where(m => m.Tipo == TipoMovimientoUbicacion.CambiarDeHueco).Sum(m => m.Cantidad);
                    resultado.Estado = EstadoCambioHueco.Cambiado;

                    if (ensayo)
                    {
                        resultado.FilasDespues = await FotoCambioHueco.Leer(db, diario.Almacen, producto, huecoOrigen, huecoDestino, desde).ConfigureAwait(false);
                        resultado.Mensaje = "ENSAYO (no se ha guardado nada): " + FotoCambioHueco.Cuadre(resultado.FilasAntes, resultado.FilasDespues, resultado.Movidas);
                        transaccion.Rollback();
                    }
                    else
                    {
                        transaccion.Commit();
                    }
                    return resultado;
                }
                catch (NestoBusinessException ex)
                {
                    // Lo dice la puerta: no queda reservado o no hay libre bastante (no ha escrito nada; y se deshace igual)
                    return NoSePuede(resultado, ex.Message);
                }
                catch (Exception ex) when (ensayo)
                {
                    resultado.Estado = EstadoCambioHueco.NoSePuede;
                    resultado.ErrorEnsayo = ServicioSalidas.MensajeCompleto(ex);
                    resultado.Mensaje = "ENSAYO: ha fallado y no se ha guardado nada. " + resultado.ErrorEnsayo;
                    return resultado;
                }
            }
        }

        private static ResultadoCambioHueco NoSePuede(ResultadoCambioHueco resultado, string mensaje)
        {
            resultado.Estado = EstadoCambioHueco.NoSePuede;
            resultado.Movidas = 0;
            resultado.Mensaje = resultado.Ensayo ? "ENSAYO (no se ha guardado nada): " + mensaje : mensaje;
            return resultado;
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }

    public interface IServicioCambioHuecoReposicion
    {
        Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int traspaso, string producto, string hueco);
        Task<ResultadoCambioHueco> Cambiar(string empresa, int traspaso, CambiarHuecoPickingDTO cambio, string usuario, bool ensayo = false);
    }

    public class ServicioCambioHuecoReposicion : IServicioCambioHuecoReposicion
    {
        private readonly IRepositorioCambioHuecoReposicion repositorio;
        private readonly ILectorOrdenRecorrido ordenRecorrido;

        public ServicioCambioHuecoReposicion(IRepositorioCambioHuecoReposicion repositorio, ILectorOrdenRecorrido ordenRecorrido = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ordenRecorrido = ordenRecorrido;
        }

        public async Task<List<HuecoAlternativoDTO>> LeerAlternativas(string empresa, int traspaso, string producto, string hueco)
        {
            if (string.IsNullOrWhiteSpace(producto) || traspaso <= 0)
            {
                return new List<HuecoAlternativoDTO>();
            }
            List<HuecoAlternativoDTO> alternativas = await repositorio.LeerAlternativas(empresa, traspaso, producto.Trim(), PlanificadorFaltasSalida.NormalizarHueco(hueco))
                .ConfigureAwait(false);
            return await ServicioCambioHuecoPicking.EnElOrdenDelRecorrido(alternativas, ordenRecorrido, empresa,
                () => repositorio.LeerAlmacenOrigen(empresa, traspaso)).ConfigureAwait(false);
        }

        public async Task<ResultadoCambioHueco> Cambiar(string empresa, int traspaso, CambiarHuecoPickingDTO cambio, string usuario, bool ensayo = false)
        {
            ResultadoCambioHueco noValido = ServicioCambioHuecoPicking.Validar(cambio, out string origen, out string destino);
            if (noValido != null)
            {
                return noValido;
            }
            ResultadoCambioHueco resultado = await repositorio.Cambiar(empresa, traspaso, cambio.Producto.Trim(), origen, destino, cambio.Cantidad, usuario, ensayo)
                .ConfigureAwait(false);
            if (resultado != null && resultado.Estado == EstadoCambioHueco.Cambiado && !resultado.Ensayo && string.IsNullOrWhiteSpace(resultado.Mensaje))
            {
                resultado.Mensaje = $"{ServicioSalidas.Unidades(resultado.Movidas)} se coge{(resultado.Movidas == 1 ? string.Empty : "n")} del hueco " +
                    $"{ServicioSalidas.Hueco(destino)}; lo que no estaba en el {ServicioSalidas.Hueco(origen)} pasa a «pendiente de ubicar».";
            }
            return resultado ?? new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoSePuede, Mensaje = "No se ha cambiado nada." };
        }
    }
}
