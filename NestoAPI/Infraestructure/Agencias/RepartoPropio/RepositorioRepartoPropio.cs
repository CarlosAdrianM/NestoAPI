using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#739: la tabla EnviosAgenciaPruebasEntrega (Scripts/Issue739_RepartoPropio.sql) va por SQL, fuera del EDMX; los
    /// envíos y su historia, por EF como siempre. La firma solo se lee en <see cref="LeerFirma"/>.
    /// </summary>
    public class RepositorioRepartoPropio : IRepositorioRepartoPropio
    {
        public const string TABLA = "EnviosAgenciaPruebasEntrega";
        /// <summary>Las columnas de la ampliación del 29/10/26 (caja del repartidor y cobro por enlace): van juntas en el script.</summary>
        internal static readonly string[] COLUMNAS_COBRO = { "CuentaCaja", "PagoTPVId", "UrlPago" };

        private const string COLUMNAS_FIJAS = "Id, EnvioId, ClaveIdempotencia, Tipo, FechaHora, Receptor, DniReceptor, CobroReembolso, ImporteCobrado, " +
            "Motivo, Texto, Latitud, Longitud, Asiento, Usuario, FechaRegistro, CAST(CASE WHEN Firma IS NULL THEN 0 ELSE 1 END AS bit) AS TieneFirma";

        // SqlQuery<PruebaEntregaReparto> exige una columna por propiedad: sin las columnas del cobro (script de antes), NULL.
        private string COLUMNAS => COLUMNAS_FIJAS + (TieneColumnasCobro()
            ? ", CuentaCaja, PagoTPVId, UrlPago"
            : ", CAST(NULL AS varchar(15)) AS CuentaCaja, CAST(NULL AS int) AS PagoTPVId, CAST(NULL AS varchar(200)) AS UrlPago");

        private bool TieneColumnasCobro() => ColumnasOpcionales.Existen(db, TABLA, COLUMNAS_COBRO);

        private readonly NVEntities db;

        public RepositorioRepartoPropio(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public Task<List<EnviosAgencia>> LeerEnviosEnReparto(DateTime desde, DateTime hasta)
        {
            short tramitado = Constantes.Agencias.ESTADO_TRAMITADO;
            short incidentado = Constantes.Agencias.ESTADO_INCIDENTADO;
            return db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                    && (e.Estado == tramitado || e.Estado == incidentado)
                    && e.Fecha >= desde && e.Fecha < hasta)
                .ToListAsync();
        }

        public async Task<EnviosAgencia> LeerEnvio(int numero)
        {
            // Where + ToListAsync, nunca SingleOrDefaultAsync con Include: ver el «Limit1» de TramitacionEnviosService.
            List<EnviosAgencia> envios = await db.EnviosAgencias
                .Include(e => e.AgenciasTransporte)
                .Include(e => e.Empresa1)
                .Where(e => e.Numero == numero)
                .ToListAsync()
                .ConfigureAwait(false);
            return envios.FirstOrDefault();
        }

        public async Task<PruebaEntregaReparto> LeerPorClave(Guid clave)
            => (await db.Database.SqlQuery<PruebaEntregaReparto>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE ClaveIdempotencia = @p0", clave).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<PruebaEntregaReparto> LeerUltimaEntrega(int envio)
            => (await db.Database.SqlQuery<PruebaEntregaReparto>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE EnvioId = @p0 AND Tipo = @p1 ORDER BY Id DESC",
                envio, PruebaEntregaReparto.TIPO_ENTREGA).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<byte[]> LeerFirma(int envio)
            => (await db.Database.SqlQuery<byte[]>(
                $"SELECT TOP 1 Firma FROM dbo.{TABLA} WHERE EnvioId = @p0 AND Tipo = @p1 AND Firma IS NOT NULL ORDER BY Id DESC",
                envio, PruebaEntregaReparto.TIPO_ENTREGA).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<int> Registrar(PruebaEntregaReparto prueba, byte[] firma, EnviosAgencia envio, IList<EnvioHistoria> historia,
            decimal? importeAContabilizar, string usuario)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int asiento = 0;
                    if (importeAContabilizar.HasValue)
                    {
                        // La misma puerta que el reembolso de las agencias al tramitar (PreContabilidad + diario de reembolsos),
                        // contra la caja del repartidor que entrega (su CajaDefecto, que el servicio deja en la prueba), NO contra
                        // la CuentaReembolsos de la agencia 14 (Carlos 29/10/26: con dos repartidores, cada uno en su caja).
                        if (string.IsNullOrWhiteSpace(prueba.CuentaCaja))
                        {
                            throw new NestoBusinessException("No hay caja del repartidor contra la que contabilizar el reembolso.");
                        }
                        asiento = await new TramitacionEnviosService(db)
                            .ContabilizarReembolsoAsync(envio, usuario, importeAContabilizar.Value, prueba.CuentaCaja)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        prueba.CuentaCaja = null;
                    }
                    prueba.Asiento = asiento > 0 ? asiento : (int?)null;
                    bool conCobro = TieneColumnasCobro();

                    try
                    {
                        _ = await db.Database.ExecuteSqlCommandAsync(
                            $"INSERT INTO dbo.{TABLA} (EnvioId, ClaveIdempotencia, Tipo, FechaHora, Receptor, DniReceptor, Firma, CobroReembolso, " +
                            "ImporteCobrado, Motivo, Texto, Latitud, Longitud, Asiento, Usuario, FechaRegistro" + (conCobro ? ", CuentaCaja, PagoTPVId, UrlPago" : string.Empty) +
                            ") VALUES (@EnvioId, @Clave, @Tipo, @FechaHora, @Receptor, @Dni, @Firma, @Cobro, @Importe, @Motivo, @Texto, @Latitud, " +
                            "@Longitud, @Asiento, @Usuario, @FechaRegistro" + (conCobro ? ", @CuentaCaja, @PagoTPVId, @UrlPago" : string.Empty) + ")",
                            Parametro("@EnvioId", SqlDbType.Int, prueba.EnvioId),
                            Parametro("@Clave", SqlDbType.UniqueIdentifier, prueba.ClaveIdempotencia),
                            Parametro("@Tipo", SqlDbType.VarChar, prueba.Tipo),
                            Parametro("@FechaHora", SqlDbType.DateTime, prueba.FechaHora),
                            Parametro("@Receptor", SqlDbType.NVarChar, prueba.Receptor),
                            Parametro("@Dni", SqlDbType.NVarChar, prueba.DniReceptor),
                            Parametro("@Firma", SqlDbType.VarBinary, firma),
                            Parametro("@Cobro", SqlDbType.VarChar, prueba.CobroReembolso),
                            Parametro("@Importe", SqlDbType.Decimal, prueba.ImporteCobrado),
                            Parametro("@Motivo", SqlDbType.VarChar, prueba.Motivo),
                            Parametro("@Texto", SqlDbType.NVarChar, prueba.Texto),
                            Parametro("@Latitud", SqlDbType.Decimal, prueba.Latitud),
                            Parametro("@Longitud", SqlDbType.Decimal, prueba.Longitud),
                            Parametro("@Asiento", SqlDbType.Int, prueba.Asiento),
                            Parametro("@Usuario", SqlDbType.VarChar, Recortar(prueba.Usuario, 50)),
                            Parametro("@FechaRegistro", SqlDbType.DateTime, prueba.FechaRegistro),
                            // Sin las columnas estos parámetros sobran pero no molestan (no aparecen en el texto del INSERT).
                            Parametro("@CuentaCaja", SqlDbType.VarChar, Recortar(prueba.CuentaCaja?.Trim(), 15)),
                            Parametro("@PagoTPVId", SqlDbType.Int, prueba.PagoTPVId),
                            Parametro("@UrlPago", SqlDbType.VarChar, Recortar(prueba.UrlPago, 200))).ConfigureAwait(false);
                    }
                    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                    {
                        throw new ClaveIdempotenciaRepetidaException(ex);
                    }

                    foreach (EnvioHistoria fila in historia ?? new List<EnvioHistoria>())
                    {
                        _ = db.EnviosHistorias.Add(fila);
                    }
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return asiento;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        private static SqlParameter Parametro(string nombre, SqlDbType tipo, object valor)
        {
            var parametro = new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };
            if (tipo == SqlDbType.Decimal)
            {
                parametro.Precision = 18;
                parametro.Scale = 6;
            }
            if (tipo == SqlDbType.VarBinary)
            {
                parametro.Size = -1;
            }
            return parametro;
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
