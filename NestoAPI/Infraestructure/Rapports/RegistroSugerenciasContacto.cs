using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#603: la tabla SugerenciasContacto. Se registra UNA vez por vendedor y día (la primera consulta inserta; las
    /// siguientes reutilizan las filas y solo añaden si piden más) y se marca Atendida cuando hay un rapport del cliente
    /// ese mismo día. Con esto se sabe quién usa la lista y si las sugerencias venden más que las llamadas libres.
    /// </summary>
    public class RegistroSugerenciasContacto
    {
        public const int LONGITUD_USUARIO = 30;

        private readonly NVEntities db;
        private readonly IBloqueoSugerenciasDelDia bloqueo;

        /// <param name="bloqueo">Serializa el registro del día de un vendedor. Null = sp_getapplock en la BD.</param>
        public RegistroSugerenciasContacto(NVEntities db, IBloqueoSugerenciasDelDia bloqueo = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.bloqueo = bloqueo ?? new BloqueoSqlSugerenciasDelDia(db);
        }

        /// <summary>
        /// Las sugerencias del día del vendedor, una por cliente/contacto: si hubiera duplicados (los de antes del fix de
        /// la carrera de NestoAPI#603) se devuelve la de menor Id y la otra no sale.
        /// </summary>
        public async Task<List<SugerenciaContacto>> LeerDelDia(string vendedor, DateTime dia)
        {
            string vendedorLimpio = vendedor?.Trim();
            DateTime desde = dia.Date;
            DateTime hasta = desde.AddDays(1);
            List<SugerenciaContacto> filas = await db.SugerenciasContacto
                .Where(s => s.Vendedor == vendedorLimpio && s.Fecha >= desde && s.Fecha < hasta)
                .ToListAsync().ConfigureAwait(false);
            return filas
                .GroupBy(s => ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(s => s.Id).First())
                .OrderBy(s => s.Orden)
                .ThenBy(s => s.Id)
                .ToList();
        }

        /// <summary>
        /// NestoAPI#603: completa la lista del día de forma atómica. Nesto abre Rapports con dos llamadas casi a la vez y,
        /// sin bloqueo, las dos veían la lista vacía e insertaban la lista entera. Dentro del bloqueo (applock por
        /// vendedor y día) se vuelve a leer lo que hay y solo se añade lo que <paramref name="elegirNuevas"/> diga que
        /// falta. Si aun así salta el índice único (UX_SugerenciasContacto_Dia), es que otro ya lo registró: se
        /// descarta lo nuestro y se devuelve lo guardado. Guarda también los cambios pendientes (atendidas).
        /// </summary>
        public async Task<List<SugerenciaContacto>> CompletarDelDia(string vendedor, DateTime dia,
            Func<List<SugerenciaContacto>, List<SugerenciaContacto>> elegirNuevas)
        {
            List<SugerenciaContacto> anadidas = new List<SugerenciaContacto>();
            using (IBloqueoDelDia bloqueoDelDia = await bloqueo.Bloquear(vendedor?.Trim(), dia.Date).ConfigureAwait(false))
            {
                List<SugerenciaContacto> delDia = await LeerDelDia(vendedor, dia).ConfigureAwait(false);
                _ = await ConciliarAtendidas(delDia, dia).ConfigureAwait(false);
                anadidas = elegirNuevas(delDia) ?? new List<SugerenciaContacto>();
                try
                {
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    bloqueoDelDia.Confirmar();
                    delDia.AddRange(anadidas);
                    return delDia;
                }
                catch (DbUpdateException ex) when (EsViolacionDeIndiceUnico(ex))
                {
                    // Al salir del using se deshace la transacción: lo nuestro no se guardó y se quita del contexto.
                }
            }
            foreach (SugerenciaContacto fila in anadidas)
            {
                _ = db.SugerenciasContacto.Remove(fila);
            }
            return await LeerDelDia(vendedor, dia).ConfigureAwait(false);
        }

        // 2601 (índice único) y 2627 (constraint única) de SQL Server.
        private static bool EsViolacionDeIndiceUnico(DbUpdateException ex)
        {
            Exception raiz = ex.GetBaseException();
            return raiz is System.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
        }

        /// <summary>
        /// Las sugerencias del día de las que ya hay rapport (de cualquier vendedor o usuario: es el cliente el que se
        /// atendió) pasan a Atendida. Cubre los rapports que no entran por el POST de SeguimientosClientes.
        /// Devuelve true si ha cambiado alguna (falta guardar).
        /// </summary>
        public async Task<bool> ConciliarAtendidas(IList<SugerenciaContacto> delDia, DateTime dia)
        {
            List<SugerenciaContacto> pendientes = delDia.Where(s => !s.Atendida).ToList();
            if (!pendientes.Any())
            {
                return false;
            }
            DateTime desde = dia.Date;
            DateTime hasta = desde.AddDays(1);
            List<string> clientes = pendientes.Select(s => s.Cliente.Trim()).Distinct().ToList();
            var rapports = await db.SeguimientosClientes
                .Where(r => r.Fecha >= desde && r.Fecha < hasta && clientes.Contains(r.Número))
                .Select(r => new { r.NºOrden, r.Número, r.Contacto, r.Fecha })
                .ToListAsync().ConfigureAwait(false);

            bool cambios = false;
            foreach (SugerenciaContacto sugerencia in pendientes)
            {
                var rapport = rapports
                    .Where(r => r.Número?.Trim() == sugerencia.Cliente.Trim() && r.Contacto?.Trim() == sugerencia.Contacto.Trim())
                    .OrderBy(r => r.Fecha)
                    .FirstOrDefault();
                if (rapport != null)
                {
                    Marcar(sugerencia, rapport.NºOrden, rapport.Fecha);
                    cambios = true;
                }
            }
            return cambios;
        }

        /// <summary>Añade las sugerencias nuevas (aún sin guardar) con Orden a continuación de las que ya hubiera.</summary>
        public List<SugerenciaContacto> Anadir(IEnumerable<SugerenciaContactoDTO> nuevas, int ordenInicial, string vendedor, string usuario, DateTime ahora)
        {
            var filas = new List<SugerenciaContacto>();
            int orden = ordenInicial;
            foreach (SugerenciaContactoDTO dto in nuevas)
            {
                var fila = new SugerenciaContacto
                {
                    Fecha = ahora,
                    Vendedor = vendedor?.Trim(),
                    Usuario = Recortar(string.IsNullOrWhiteSpace(usuario) ? "(desconocido)" : usuario.Trim(), LONGITUD_USUARIO),
                    Cliente = dto.Cliente,
                    Contacto = dto.Contacto,
                    Prioridad = dto.Prioridad,
                    Orden = ++orden,
                    Probabilidad = dto.Probabilidad,
                    Motivo = Recortar(dto.Motivo ?? string.Empty, MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO),
                    Atendida = false
                };
                _ = db.SugerenciasContacto.Add(fila);
                filas.Add(fila);
            }
            return filas;
        }

        public Task<int> Guardar() => db.SaveChangesAsync();

        /// <summary>
        /// Al crear un rapport: si el cliente tenía sugerencia ese día sin atender, se marca Atendida con el rapport.
        /// Guarda. Devuelve cuántas ha marcado.
        /// </summary>
        public async Task<int> MarcarAtendidaPorRapport(string cliente, string contacto, DateTime fechaRapport, int rapportId)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return 0;
            }
            string clienteLimpio = cliente.Trim();
            string contactoLimpio = contacto?.Trim() ?? string.Empty;
            DateTime desde = fechaRapport.Date;
            DateTime hasta = desde.AddDays(1);
            List<SugerenciaContacto> filas = await db.SugerenciasContacto
                .Where(s => s.Cliente == clienteLimpio && s.Contacto == contactoLimpio && s.Fecha >= desde && s.Fecha < hasta && !s.Atendida)
                .ToListAsync().ConfigureAwait(false);
            if (!filas.Any())
            {
                return 0;
            }
            foreach (SugerenciaContacto fila in filas)
            {
                Marcar(fila, rapportId, fechaRapport);
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            return filas.Count;
        }

        private static void Marcar(SugerenciaContacto fila, int rapportId, DateTime fechaRapport)
        {
            fila.Atendida = true;
            fila.RapportId = rapportId;
            fila.FechaAtendida = fechaRapport;
        }

        internal static string Recortar(string texto, int longitud) => texto.Length > longitud ? texto.Substring(0, longitud) : texto;
    }

    /// <summary>NestoAPI#603: serializa el registro de la lista del día de un vendedor.</summary>
    public interface IBloqueoSugerenciasDelDia
    {
        Task<IBloqueoDelDia> Bloquear(string vendedor, DateTime dia);
    }

    /// <summary>El bloqueo abierto (una transacción): Confirmar = commit; Dispose sin confirmar = rollback.</summary>
    public interface IBloqueoDelDia : IDisposable
    {
        void Confirmar();
    }

    /// <summary>
    /// sp_getapplock ligado a una transacción (se libera solo en commit/rollback), recurso
    /// SugerenciasContacto:{empresa}:{vendedor}:{yyyyMMdd}, espera máxima 10 s.
    /// </summary>
    internal sealed class BloqueoSqlSugerenciasDelDia : IBloqueoSugerenciasDelDia
    {
        private readonly NVEntities db;

        public BloqueoSqlSugerenciasDelDia(NVEntities db)
        {
            this.db = db;
        }

        public async Task<IBloqueoDelDia> Bloquear(string vendedor, DateTime dia)
        {
            string recurso = $"SugerenciasContacto:{Constantes.Empresas.EMPRESA_POR_DEFECTO}:{vendedor}:{dia:yyyyMMdd}";
            DbContextTransaction transaccion = db.Database.BeginTransaction();
            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(
                    @"DECLARE @resultado int;
                      EXEC @resultado = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                      IF @resultado < 0 RAISERROR('No se pudo obtener el bloqueo de las sugerencias de contacto del día; reintente en unos segundos.', 16, 1);",
                    recurso).ConfigureAwait(false);
            }
            catch
            {
                transaccion.Dispose();
                throw;
            }
            return new Transaccion(transaccion);
        }

        private sealed class Transaccion : IBloqueoDelDia
        {
            private readonly DbContextTransaction transaccion;

            public Transaccion(DbContextTransaction transaccion)
            {
                this.transaccion = transaccion;
            }

            public void Confirmar() => transaccion.Commit();

            public void Dispose() => transaccion.Dispose();
        }
    }
}
