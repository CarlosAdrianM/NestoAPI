using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#906: la discusión de una diferencia (dbo.DiferenciasReposicionDiscusiones).</summary>
    public class DiscusionDiferenciaDTO
    {
        public int Id { get; set; }
        public int DiferenciaId { get; set; }
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Producto { get; set; }
        public int Unidades { get; set; }
        public string Estado { get; set; }
        public string AbiertaPor { get; set; }
        public DateTime FechaApertura { get; set; }
        public string ComentarioOrigen { get; set; }
        public DateTime Plazo { get; set; }
        public DateTime? RecordatorioEnviado { get; set; }
        public string Respuesta { get; set; }
        public string RespondidaPor { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public string ComentarioDestino { get; set; }
        public DateTime? FechaCierre { get; set; }
        /// <summary>Qué decidió la regla (o cómo se pusieron de acuerdo), en palabras del almacén.</summary>
        public string Resumen { get; set; }
        /// <summary>NestoAPI#906: los bultos de la reposición y cuáles tienen foto de cuando se envió (vacía sin el script de las fotos).</summary>
        public List<BultoConFotoReposicionDTO> Bultos { get; set; } = new List<BultoConFotoReposicionDTO>();
    }

    /// <summary>NestoAPI#906: una unidad en desacuerdo con sus rasgos, la decisión de la regla y la verdad (dbo.DiferenciasReposicionImputaciones).</summary>
    public class ImputacionDesacuerdoFila
    {
        public int Id { get; set; }
        public int DiscusionId { get; set; }
        public int DiferenciaId { get; set; }
        public short Unidad { get; set; }
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Producto { get; set; }
        public decimal? Pvp { get; set; }
        public string Preparador { get; set; }
        public string UsuarioRecepcion { get; set; }
        public string Repartidor { get; set; }
        public bool LecturaOrigen { get; set; }
        public int EscaneadasOrigen { get; set; }
        public int Enviado { get; set; }
        public bool LecturaDestino { get; set; }
        public short Bultos { get; set; }
        public short BultosEnDestino { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public decimal? IndicadorOrigen { get; set; }
        public decimal? IndicadorDestino { get; set; }
        public string AlmacenImputado { get; set; }
        public string Paso { get; set; }
        public string Motivo { get; set; }
        public DateTime? FechaDecision { get; set; }
        public string ErrorDemostradoDe { get; set; }
        public string ComoSeDemostro { get; set; }
        public DateTime? FechaDemostracion { get; set; }
        public int? InventarioDemostracion { get; set; }
        public bool? ReglaAcerto { get; set; }
    }

    public class SobranteInventario
    {
        public int NumOrden { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public DateTime Fecha { get; set; }
        public int Sobrante { get; set; }
    }

    public interface IRepositorioDiscusionesDiferencias
    {
        bool TablasCreadas();
        Task<DiscusionDiferenciaDTO> LeerPorDiferencia(string empresa, int diferenciaId);
        Task<List<DiscusionDiferenciaDTO>> LeerPorDiferencias(string empresa, IEnumerable<int> diferencias);
        Task<List<DiscusionDiferenciaDTO>> Listar(string empresa, string almacen, bool soloAbiertas);
        Task<List<DiscusionDiferenciaDTO>> Abiertas(string empresa);
        Task<int> Abrir(DiscusionDiferenciaDTO discusion, IEnumerable<ImputacionDesacuerdoFila> unidades);
        Task GuardarRespuesta(int id, string respuesta, string usuario, string comentario);
        Task MarcarRecordatorio(int id);
        /// <summary>Acordada (sin decisión de la regla), con la verdad de todas sus unidades si se sabe.</summary>
        Task Acordar(int id, string resumen, string errorDe, string comoSeDemostro);
        Task GuardarDecision(int id, string resumen, IEnumerable<ImputacionUnidadDesacuerdo> decisiones, decimal? indicadorOrigen, decimal? indicadorDestino);
        Task<List<ImputacionDesacuerdoFila>> Unidades(int discusionId);
        Task<PruebasDesacuerdo> LeerPruebas(string empresa, int traspaso, string producto, int enviado);
        Task<decimal?> LeerPvp(string empresa, string producto);
        Task<string> LeerRepartidor(string empresa, int traspaso);
        Task<List<DiferenciaParaIndicador>> LeerParaIndicador(string empresa, DateTime desde);
        Task<string> UltimoTurno(string almacenA, string almacenB);
        Task<List<ImputacionDesacuerdoFila>> Imputaciones(DateTime desde);
        Task<List<SobranteInventario>> Sobrantes(string empresa, IEnumerable<string> productos, DateTime desde);
        Task GuardarVerdad(int discusionId, string almacen, string comoSeDemostro, DateTime fecha, int? inventario);
    }

    /// <summary>
    /// NestoAPI#906: SQL a mano (fuera del EDMX, Scripts/Issue906_DiferenciasReposicionDiscusiones.sql). Comparte el contexto con
    /// <see cref="RepositorioDiferenciasReposicionSql"/>: lo que se escribe dentro de su transacción va en ella.
    /// </summary>
    public class RepositorioDiscusionesDiferenciasSql : IRepositorioDiscusionesDiferencias
    {
        internal const string TABLA = "DiferenciasReposicionDiscusiones";
        internal const string TABLA_UNIDADES = "DiferenciasReposicionImputaciones";

        private const string SELECT_DISCUSION = @"
SELECT Id, DiferenciaId, RTRIM(Empresa) AS Empresa, NumTraspaso AS Traspaso, RTRIM(Origen) AS Origen, RTRIM(Destino) AS Destino,
       RTRIM(Producto) AS Producto, Unidades, Estado, AbiertaPor, FechaApertura, ComentarioOrigen, Plazo, RecordatorioEnviado, Respuesta,
       RespondidaPor, FechaRespuesta, ComentarioDestino, FechaCierre, Resumen
FROM dbo.DiferenciasReposicionDiscusiones";

        private const string SELECT_UNIDAD = @"
SELECT i.Id, i.DiscusionId, d.DiferenciaId, i.Unidad, RTRIM(d.Empresa) AS Empresa, d.NumTraspaso AS Traspaso, RTRIM(d.Origen) AS Origen,
       RTRIM(d.Destino) AS Destino, RTRIM(d.Producto) AS Producto, i.Pvp, i.Preparador, i.UsuarioRecepcion, i.Repartidor, i.LecturaOrigen,
       i.EscaneadasOrigen, i.Enviado, i.LecturaDestino, i.Bultos, i.BultosEnDestino, i.FechaRecepcion, i.IndicadorOrigen, i.IndicadorDestino,
       RTRIM(i.AlmacenImputado) AS AlmacenImputado, i.Paso, i.Motivo, i.FechaDecision, RTRIM(i.ErrorDemostradoDe) AS ErrorDemostradoDe,
       i.ComoSeDemostro, i.FechaDemostracion, i.InventarioDemostracion, i.ReglaAcerto
FROM dbo.DiferenciasReposicionImputaciones i
INNER JOIN dbo.DiferenciasReposicionDiscusiones d ON d.Id = i.DiscusionId";

        private readonly NVEntities db;

        public RepositorioDiscusionesDiferenciasSql(NVEntities db)
        {
            this.db = db;
        }

        public bool TablasCreadas() => ColumnasOpcionales.ExisteTabla(db, TABLA) && ColumnasOpcionales.ExisteTabla(db, TABLA_UNIDADES);

        public Task<DiscusionDiferenciaDTO> LeerPorDiferencia(string empresa, int diferenciaId)
            => db.Database.SqlQuery<DiscusionDiferenciaDTO>(SELECT_DISCUSION + " WHERE Empresa = @p0 AND DiferenciaId = @p1",
                DiferenciasReposicionSql.Char("@p0", empresa, 3), Entero("@p1", diferenciaId)).FirstOrDefaultAsync();

        public async Task<List<DiscusionDiferenciaDTO>> LeerPorDiferencias(string empresa, IEnumerable<int> diferencias)
        {
            List<int> ids = (diferencias ?? Enumerable.Empty<int>()).Distinct().ToList();
            var resultado = new List<DiscusionDiferenciaDTO>();
            foreach (List<int> lote in Lotes(ids, 500))
            {
                string lista = string.Join(", ", lote.Select((x, i) => "@d" + i));
                var parametros = new List<object> { DiferenciasReposicionSql.Char("@p0", empresa, 3) };
                parametros.AddRange(lote.Select((x, i) => (object)Entero("@d" + i, x)));
                resultado.AddRange(await db.Database.SqlQuery<DiscusionDiferenciaDTO>(
                    SELECT_DISCUSION + $" WHERE Empresa = @p0 AND DiferenciaId IN ({lista})", parametros.ToArray()).ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public Task<List<DiscusionDiferenciaDTO>> Listar(string empresa, string almacen, bool soloAbiertas)
            => db.Database.SqlQuery<DiscusionDiferenciaDTO>("SELECT TOP 500 * FROM (" + SELECT_DISCUSION + @") x
WHERE x.Empresa = @p0 AND (@p1 IS NULL OR x.Origen = @p1 OR x.Destino = @p1) AND (@p2 = 0 OR x.Estado = 'Abierta')
ORDER BY CASE WHEN x.Estado = 'Abierta' THEN 0 ELSE 1 END, x.Plazo, x.Id DESC",
                DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", almacen, 3),
                new SqlParameter("@p2", SqlDbType.Bit) { Value = soloAbiertas }).ToListAsync();

        public Task<List<DiscusionDiferenciaDTO>> Abiertas(string empresa)
            => db.Database.SqlQuery<DiscusionDiferenciaDTO>(SELECT_DISCUSION + " WHERE Empresa = @p0 AND Estado = 'Abierta' ORDER BY Plazo",
                DiferenciasReposicionSql.Char("@p0", empresa, 3)).ToListAsync();

        private const string SQL_INSERTAR_DISCUSION = @"
IF NOT EXISTS (SELECT 1 FROM dbo.DiferenciasReposicionDiscusiones WITH (UPDLOCK, HOLDLOCK) WHERE DiferenciaId = @p0)
BEGIN
    INSERT INTO dbo.DiferenciasReposicionDiscusiones (DiferenciaId, Empresa, NumTraspaso, Origen, Destino, Producto, Unidades, Estado, AbiertaPor,
        ComentarioOrigen, Plazo)
    VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, 'Abierta', @p7, @p8, @p9);
    SELECT CAST(SCOPE_IDENTITY() AS int);
END
ELSE
    SELECT CAST(0 AS int);";

        private const string SQL_INSERTAR_UNIDAD = @"
INSERT INTO dbo.DiferenciasReposicionImputaciones (DiscusionId, Unidad, Pvp, Preparador, UsuarioRecepcion, Repartidor, LecturaOrigen,
    EscaneadasOrigen, Enviado, LecturaDestino, Bultos, BultosEnDestino, FechaRecepcion, IndicadorOrigen, IndicadorDestino)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14)";

        public async Task<int> Abrir(DiscusionDiferenciaDTO d, IEnumerable<ImputacionDesacuerdoFila> unidades)
        {
            using (DbContextTransaction transaccion = db.Database.CurrentTransaction == null ? db.Database.BeginTransaction() : null)
            {
                List<int> ids = await db.Database.SqlQuery<int>(SQL_INSERTAR_DISCUSION,
                    Entero("@p0", d.DiferenciaId), DiferenciasReposicionSql.Char("@p1", d.Empresa, 3), Entero("@p2", d.Traspaso),
                    DiferenciasReposicionSql.Char("@p3", d.Origen, 3), DiferenciasReposicionSql.Char("@p4", d.Destino, 3),
                    DiferenciasReposicionSql.VarChar("@p5", d.Producto, 15), Entero("@p6", d.Unidades),
                    DiferenciasReposicionSql.VarChar("@p7", d.AbiertaPor, 50), DiferenciasReposicionSql.NVarChar("@p8", d.ComentarioOrigen, 500),
                    new SqlParameter("@p9", SqlDbType.DateTime) { Value = d.Plazo }).ToListAsync().ConfigureAwait(false);
                int id = ids.FirstOrDefault();
                if (id > 0)
                {
                    foreach (ImputacionDesacuerdoFila u in unidades ?? Enumerable.Empty<ImputacionDesacuerdoFila>())
                    {
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_UNIDAD,
                            Entero("@p0", id), new SqlParameter("@p1", SqlDbType.SmallInt) { Value = u.Unidad },
                            Decimal("@p2", u.Pvp), DiferenciasReposicionSql.VarChar("@p3", u.Preparador, 50),
                            DiferenciasReposicionSql.VarChar("@p4", u.UsuarioRecepcion, 50), DiferenciasReposicionSql.VarChar("@p5", u.Repartidor, 50),
                            new SqlParameter("@p6", SqlDbType.Bit) { Value = u.LecturaOrigen }, Entero("@p7", u.EscaneadasOrigen), Entero("@p8", u.Enviado),
                            new SqlParameter("@p9", SqlDbType.Bit) { Value = u.LecturaDestino },
                            new SqlParameter("@p10", SqlDbType.SmallInt) { Value = u.Bultos }, new SqlParameter("@p11", SqlDbType.SmallInt) { Value = u.BultosEnDestino },
                            new SqlParameter("@p12", SqlDbType.DateTime) { Value = (object)u.FechaRecepcion ?? DBNull.Value },
                            Decimal("@p13", u.IndicadorOrigen), Decimal("@p14", u.IndicadorDestino)).ConfigureAwait(false);
                    }
                }
                transaccion?.Commit();
                return id;
            }
        }

        public Task GuardarRespuesta(int id, string respuesta, string usuario, string comentario)
            => db.Database.ExecuteSqlCommandAsync(@"
UPDATE dbo.DiferenciasReposicionDiscusiones SET Respuesta = @p1, RespondidaPor = @p2, FechaRespuesta = GETDATE(), ComentarioDestino = @p3
WHERE Id = @p0 AND Estado = 'Abierta'",
                Entero("@p0", id), DiferenciasReposicionSql.VarChar("@p1", respuesta, 20), DiferenciasReposicionSql.VarChar("@p2", usuario, 50),
                DiferenciasReposicionSql.NVarChar("@p3", comentario, 500));

        public Task MarcarRecordatorio(int id)
            => db.Database.ExecuteSqlCommandAsync("UPDATE dbo.DiferenciasReposicionDiscusiones SET RecordatorioEnviado = GETDATE() WHERE Id = @p0", Entero("@p0", id));

        public async Task Acordar(int id, string resumen, string errorDe, string comoSeDemostro)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(@"
UPDATE dbo.DiferenciasReposicionDiscusiones SET Estado = 'Acordada', FechaCierre = GETDATE(), Resumen = @p1 WHERE Id = @p0 AND Estado = 'Abierta'",
                Entero("@p0", id), DiferenciasReposicionSql.NVarChar("@p1", resumen, 1000)).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(errorDe))
            {
                await GuardarVerdad(id, errorDe, comoSeDemostro, DateTime.Now, null).ConfigureAwait(false);
            }
        }

        public async Task GuardarDecision(int id, string resumen, IEnumerable<ImputacionUnidadDesacuerdo> decisiones, decimal? indicadorOrigen, decimal? indicadorDestino)
        {
            foreach (ImputacionUnidadDesacuerdo d in decisiones ?? Enumerable.Empty<ImputacionUnidadDesacuerdo>())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(@"
UPDATE dbo.DiferenciasReposicionImputaciones
SET AlmacenImputado = @p2, Paso = @p3, Motivo = @p4, FechaDecision = GETDATE(), IndicadorOrigenDecision = @p5, IndicadorDestinoDecision = @p6
WHERE DiscusionId = @p0 AND Unidad = @p1",
                    Entero("@p0", id), new SqlParameter("@p1", SqlDbType.SmallInt) { Value = (short)d.Unidad },
                    DiferenciasReposicionSql.Char("@p2", d.Almacen, 3), DiferenciasReposicionSql.VarChar("@p3", d.Paso, 12),
                    DiferenciasReposicionSql.NVarChar("@p4", d.Motivo, 500), Decimal("@p5", indicadorOrigen), Decimal("@p6", indicadorDestino)).ConfigureAwait(false);
            }
            int cambiadas = await db.Database.ExecuteSqlCommandAsync(@"
UPDATE dbo.DiferenciasReposicionDiscusiones SET Estado = 'Decidida', FechaCierre = GETDATE(), Resumen = @p1 WHERE Id = @p0 AND Estado = 'Abierta'",
                Entero("@p0", id), DiferenciasReposicionSql.NVarChar("@p1", resumen, 1000)).ConfigureAwait(false);
            if (cambiadas != 1)
            {
                throw new Exceptions.NestoBusinessException("La discusión ya no está abierta: no se ha hecho nada.") { StatusCode = System.Net.HttpStatusCode.Conflict };
            }
        }

        public Task<List<ImputacionDesacuerdoFila>> Unidades(int discusionId)
            => db.Database.SqlQuery<ImputacionDesacuerdoFila>(SELECT_UNIDAD + " WHERE i.DiscusionId = @p0 ORDER BY i.Unidad", Entero("@p0", discusionId)).ToListAsync();

        // Lo leído al preparar la salida (Ariadna, «recoger» y empaquetar): la fase con más unidades, solo por escáner
        internal const string SQL_ESCANEADAS = @"
SELECT CAST(ISNULL(MAX(s.Total), 0) AS int) FROM (
    SELECT e.Fase, SUM(e.Cantidad) AS Total FROM dbo.PreparacionEscaneos e
    WHERE e.Empresa = @p0 AND e.TipoOrigen = 'REPO' AND e.NumeroOrigen = @p1 AND e.Producto = @p2 AND e.Fase IN ('PICK', 'PACK') AND e.Metodo = 'SCAN'
    GROUP BY e.Fase) s";

        internal const string SQL_BULTOS = @"
SELECT RTRIM(b.Codigo) AS Codigo, b.FechaLectura AS LeidoAlRecibir, {ENTREGADO} AS Entregado
FROM dbo.ReposicionesBultos b WHERE b.Empresa = @p0 AND b.NumTraspaso = @p1";

        internal const string SQL_ENTREGADO = "(SELECT MIN(m.FechaHora) FROM dbo.RepartoBultosMovimientos m WHERE m.Codigo = b.Codigo AND m.Tipo = 'Entregado')";

        public async Task<PruebasDesacuerdo> LeerPruebas(string empresa, int traspaso, string producto, int enviado)
        {
            var pruebas = new PruebasDesacuerdo { Enviado = enviado };
            if (ColumnasOpcionales.Existen(db, "PreparacionEscaneos", "TipoOrigen", "NumeroOrigen"))
            {
                pruebas.EscaneadasOrigen = await db.Database.SqlQuery<int>(SQL_ESCANEADAS, DiferenciasReposicionSql.Char("@p0", empresa, 3),
                    Entero("@p1", traspaso), DiferenciasReposicionSql.Char("@p2", producto, 15)).SingleAsync().ConfigureAwait(false);
            }
            if (ColumnasOpcionales.ExisteTabla(db, "ReposicionesBultos"))
            {
                string entregado = ColumnasOpcionales.ExisteTabla(db, "RepartoBultosMovimientos") ? SQL_ENTREGADO : "CAST(NULL AS datetime)";
                List<FilaBulto> bultos = await db.Database.SqlQuery<FilaBulto>(SQL_BULTOS.Replace("{ENTREGADO}", entregado),
                    DiferenciasReposicionSql.Char("@p0", empresa, 3), Entero("@p1", traspaso)).ToListAsync().ConfigureAwait(false);
                pruebas.Bultos = bultos.Select(b => new BultoDesacuerdo { Codigo = b.Codigo, LeidoAlRecibir = b.LeidoAlRecibir, Entregado = b.Entregado }).ToList();
            }
            return pruebas;
        }

        public Task<decimal?> LeerPvp(string empresa, string producto)
            => db.Database.SqlQuery<decimal?>("SELECT TOP 1 CAST(PVP AS decimal(10, 2)) FROM Productos WHERE Empresa = @p0 AND [Número] = @p1",
                DiferenciasReposicionSql.Char("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO, 3), DiferenciasReposicionSql.Char("@p1", producto, 15))
                .FirstOrDefaultAsync();

        public async Task<string> LeerRepartidor(string empresa, int traspaso)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, "RepartoBultosMovimientos"))
            {
                return null;
            }
            return await db.Database.SqlQuery<string>(@"
SELECT TOP 1 RTRIM(m.Usuario) FROM dbo.RepartoBultosMovimientos m
WHERE m.NumTraspaso = @p0 AND m.Tipo IN ('Recogido', 'Cargado') ORDER BY m.FechaHora DESC, m.Id DESC", Entero("@p0", traspaso))
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        private class FilaIndicador
        {
            public int Id { get; set; }
            public string Origen { get; set; }
            public string Destino { get; set; }
            public int Diferencia { get; set; }
            public string Resolucion { get; set; }
            public string Preparador { get; set; }
            public string UsuarioRecepcion { get; set; }
        }

        private class FilaBulto
        {
            public string Codigo { get; set; }
            public DateTime? LeidoAlRecibir { get; set; }
            public DateTime? Entregado { get; set; }
        }

        private class ErrorDemostradoFila
        {
            public int DiferenciaId { get; set; }
            public string ErrorDe { get; set; }
        }

        // Errores demostrados de las unidades en desacuerdo: la verdad (Reconocido/Inventario) o, sin ella, lo decidido por la PRUEBA
        internal const string SQL_ERRORES_DESACUERDO = @"
SELECT x.DiferenciaId, RTRIM(CASE WHEN i.ErrorDemostradoDe IS NOT NULL THEN i.ErrorDemostradoDe ELSE i.AlmacenImputado END) AS ErrorDe
FROM dbo.DiferenciasReposicionImputaciones i
INNER JOIN dbo.DiferenciasReposicionDiscusiones x ON x.Id = i.DiscusionId
WHERE x.Empresa = @p0 AND x.FechaApertura >= @p1 AND (i.ErrorDemostradoDe IS NOT NULL OR i.Paso = 'Prueba')";

        public async Task<List<DiferenciaParaIndicador>> LeerParaIndicador(string empresa, DateTime desde)
        {
            List<FilaIndicador> leidas = await db.Database.SqlQuery<FilaIndicador>(@"
SELECT d.Id, RTRIM(d.Origen) AS Origen, RTRIM(d.Destino) AS Destino, d.Diferencia, d.Resolucion, d.Preparador, d.UsuarioRecepcion
FROM dbo.DiferenciasReposiciones d WITH (NOLOCK)
WHERE d.Empresa = @p0 AND d.FechaRecepcion >= @p1",
                DiferenciasReposicionSql.Char("@p0", empresa, 3), new SqlParameter("@p1", SqlDbType.DateTime) { Value = desde }).ToListAsync().ConfigureAwait(false);
            List<DiferenciaParaIndicador> filas = leidas.Select(f => new DiferenciaParaIndicador
            {
                Id = f.Id, Origen = f.Origen, Destino = f.Destino, Diferencia = f.Diferencia, Resolucion = f.Resolucion,
                Preparador = f.Preparador, UsuarioRecepcion = f.UsuarioRecepcion
            }).ToList();
            if (TablasCreadas())
            {
                List<ErrorDemostradoFila> errores = await db.Database.SqlQuery<ErrorDemostradoFila>(SQL_ERRORES_DESACUERDO,
                    DiferenciasReposicionSql.Char("@p0", empresa, 3), new SqlParameter("@p1", SqlDbType.DateTime) { Value = desde }).ToListAsync().ConfigureAwait(false);
                foreach (DiferenciaParaIndicador fila in filas)
                {
                    fila.ErroresDemostradosDe = errores.Where(e => e.DiferenciaId == fila.Id && !string.IsNullOrWhiteSpace(e.ErrorDe))
                        .Select(e => e.ErrorDe).Distinct().ToList();
                }
            }
            return filas;
        }

        public Task<string> UltimoTurno(string almacenA, string almacenB)
            => db.Database.SqlQuery<string>(@"
SELECT TOP 1 RTRIM(i.AlmacenImputado) FROM dbo.DiferenciasReposicionImputaciones i
INNER JOIN dbo.DiferenciasReposicionDiscusiones d ON d.Id = i.DiscusionId
WHERE i.Paso = 'Alternancia' AND ((d.Origen = @p0 AND d.Destino = @p1) OR (d.Origen = @p1 AND d.Destino = @p0))
ORDER BY i.FechaDecision DESC, i.Id DESC",
                DiferenciasReposicionSql.Char("@p0", almacenA, 3), DiferenciasReposicionSql.Char("@p1", almacenB, 3)).FirstOrDefaultAsync();

        public Task<List<ImputacionDesacuerdoFila>> Imputaciones(DateTime desde)
            => db.Database.SqlQuery<ImputacionDesacuerdoFila>(SELECT_UNIDAD + " WHERE d.FechaApertura >= @p0 ORDER BY i.Id",
                new SqlParameter("@p0", SqlDbType.DateTime) { Value = desde }).ToListAsync();

        // Inventario CONTABILIZADO (NºTraspaso) con sobrante: contado > teórico
        public async Task<List<SobranteInventario>> Sobrantes(string empresa, IEnumerable<string> productos, DateTime desde)
        {
            List<string> lista = (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            var resultado = new List<SobranteInventario>();
            foreach (List<string> lote in Lotes(lista, 500))
            {
                var parametros = new List<object>
                {
                    new SqlParameter("@p0", SqlDbType.DateTime) { Value = desde }
                };
                parametros.AddRange(lote.Select((p, i) => (object)DiferenciasReposicionSql.Char("@x" + i, p, 15)));
                resultado.AddRange(await db.Database.SqlQuery<SobranteInventario>($@"
SELECT i.[NºOrden] AS NumOrden, RTRIM(i.[Almacén]) AS Almacen, RTRIM(i.[Número]) AS Producto, i.Fecha,
       ISNULL(i.StockReal, 0) - ISNULL(i.StockCalculado, 0) AS Sobrante
FROM dbo.Inventarios i
WHERE i.Fecha >= @p0 AND i.[NºTraspaso] IS NOT NULL AND ISNULL(i.StockReal, 0) > ISNULL(i.StockCalculado, 0)
  AND i.Empresa IN ('1', '3') AND i.[Número] IN ({string.Join(", ", lote.Select((p, i) => "@x" + i))})",
                    parametros.ToArray()).ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public async Task GuardarVerdad(int discusionId, string almacen, string comoSeDemostro, DateTime fecha, int? inventario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(@"
UPDATE dbo.DiferenciasReposicionImputaciones
SET ErrorDemostradoDe = @p1, ComoSeDemostro = @p2, FechaDemostracion = @p3, InventarioDemostracion = @p4,
    ReglaAcerto = CASE WHEN AlmacenImputado IS NULL THEN NULL WHEN AlmacenImputado = @p1 THEN 1 ELSE 0 END
WHERE DiscusionId = @p0 AND ErrorDemostradoDe IS NULL",
                Entero("@p0", discusionId), DiferenciasReposicionSql.Char("@p1", almacen, 3), DiferenciasReposicionSql.VarChar("@p2", comoSeDemostro, 12),
                new SqlParameter("@p3", SqlDbType.DateTime) { Value = fecha }, DiferenciasReposicionSql.EnteroONulo("@p4", inventario)).ConfigureAwait(false);
        }

        private static SqlParameter Entero(string nombre, int valor) => new SqlParameter(nombre, SqlDbType.Int) { Value = valor };

        private static SqlParameter Decimal(string nombre, decimal? valor)
            => new SqlParameter(nombre, SqlDbType.Decimal) { Precision = 10, Scale = 4, Value = (object)valor ?? DBNull.Value };

        private static IEnumerable<List<T>> Lotes<T>(List<T> lista, int tamano)
        {
            for (int i = 0; i < lista.Count; i += tamano)
            {
                yield return lista.Skip(i).Take(tamano).ToList();
            }
        }
    }
}
