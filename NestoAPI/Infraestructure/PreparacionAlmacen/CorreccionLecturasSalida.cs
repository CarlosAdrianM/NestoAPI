using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#646 / Ariadna#41: cómo ha ido corregir una parada (POST api/Almacen/Escaneos/Corregir).</summary>
    public enum EstadoCorreccionLectura
    {
        Corregida,
        /// <summary>Ese IdCliente ya estaba: un reenvío, no se vuelve a quitar nada.</summary>
        Repetida,
        /// <summary>La parada ya no está como la vio la app (otra PDA la ha cambiado), o ya no está: no se toca nada.</summary>
        HaCambiado,
        /// <summary>La salida ya está terminada: no se toca nada.</summary>
        YaTerminada,
        NoValida
    }

    public class ResultadoCorreccionLectura
    {
        public EstadoCorreccionLectura Estado { get; set; }
        public ResultadoCorregirEscaneoDTO Respuesta { get; set; }
    }

    /// <summary>
    /// NestoAPI#646 / Ariadna#41: corregir una parada se hace todo en UNA transacción: se bloquean las lecturas de la salida, se
    /// mira cómo está la parada y, solo si sigue como la vio la app, se guarda la lectura en negativo.
    /// </summary>
    public interface IRepositorioCorrecciones
    {
        Task<T> EnTransaccion<T>(Func<ITransaccionCorreccion, Task<T>> trabajo);
    }

    public interface ITransaccionCorreccion
    {
        /// <summary>
        /// Bloquea hasta el final de la transacción las lecturas de la salida (otra corrección a la vez espera) y su marca de
        /// terminada. True si ya está terminada.
        /// </summary>
        Task<bool> BloquearYMirarSiTerminada(string empresa, string tipoOrigen, int numero);
        Task<bool> ExisteEscaneo(Guid idCliente);
        /// <summary>Lo leído y dado por falta de la salida, por producto y usuario (como SQL_LECTURAS_DE_SALIDA), ya bloqueado.</summary>
        Task<List<LecturaPickingAlmacen>> LeerLecturas(string empresa, string tipoOrigen, int numero);
        /// <summary>True si lo guarda; false si ya estaba (mismo IdCliente).</summary>
        Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario);
    }

    public class RepositorioCorreccionesSql : IRepositorioCorrecciones
    {
        private readonly NVEntities db;

        public RepositorioCorreccionesSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<T> EnTransaccion<T>(Func<ITransaccionCorreccion, Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo(new TransaccionCorreccionSql(db)).ConfigureAwait(false);
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
                        // Ya deshecha
                    }
                    throw;
                }
            }
        }
    }

    public class TransaccionCorreccionSql : ITransaccionCorreccion
    {
        // UPDLOCK + HOLDLOCK sobre el rango de las lecturas de la salida (IX_PreparacionEscaneos_Origen) y sobre su marca de
        // terminada: otra corrección (o un «Terminar») a la vez espera a que esta acabe. Si la tabla de terminadas no existe
        // (script de Ariadna#6 sin lanzar), no está terminada.
        internal const string SQL_BLOQUEAR_SALIDA = @"
DECLARE @bloqueadas int = (SELECT COUNT(*) FROM dbo.PreparacionEscaneos WITH (UPDLOCK, HOLDLOCK)
                           WHERE Empresa = @p0 AND TipoOrigen = @p1 AND NumeroOrigen = @p2 AND Fase = 'PICK');
IF OBJECT_ID('dbo.PreparacionSalidasTerminadas') IS NULL
    SELECT CAST(0 AS bit)
ELSE
    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas WITH (UPDLOCK, HOLDLOCK)
                                  WHERE Empresa = @p0 AND TipoOrigen = @p1 AND NumeroOrigen = @p2) THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_EXISTE_ESCANEO = "SELECT COUNT(*) FROM dbo.PreparacionEscaneos WHERE IdCliente = @p0";

        private readonly NVEntities db;
        private readonly RepositorioPreparacionAlmacen repositorio;

        public TransaccionCorreccionSql(NVEntities db)
        {
            this.db = db;
            repositorio = new RepositorioPreparacionAlmacen(db);
        }

        public async Task<bool> BloquearYMirarSiTerminada(string empresa, string tipoOrigen, int numero)
        {
            return await db.Database.SqlQuery<bool>(SQL_BLOQUEAR_SALIDA, empresa, tipoOrigen, numero).FirstAsync().ConfigureAwait(false);
        }

        public async Task<bool> ExisteEscaneo(Guid idCliente)
        {
            return await db.Database.SqlQuery<int>(SQL_EXISTE_ESCANEO, idCliente).FirstAsync().ConfigureAwait(false) > 0;
        }

        public Task<List<LecturaPickingAlmacen>> LeerLecturas(string empresa, string tipoOrigen, int numero)
        {
            return repositorio.LeerLecturasDeSalida(empresa, tipoOrigen, numero);
        }

        public Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario)
        {
            return repositorio.InsertarEscaneo(empresa, escaneo, usuario);
        }
    }
}
