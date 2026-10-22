using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>NestoAPI#692: un tipo de notificación push a clientes (TNV) que el cliente puede apagar por separado.</summary>
    public class TipoNotificacionCliente
    {
        public TipoNotificacionCliente(string tipo, string nombre, string descripcion)
        {
            Tipo = tipo;
            Nombre = nombre;
            Descripcion = descripcion;
        }

        /// <summary>Lo que se guarda en NotificacionesClientesBajas.Tipo y viaja en la URL del PUT de la app.</summary>
        public string Tipo { get; }
        public string Nombre { get; }
        public string Descripcion { get; }
    }

    /// <summary>
    /// NestoAPI#692 (decisión de Carlos, 22/10/26): las push a clientes que no son de su pedido, cada una con su interruptor
    /// en «Notificaciones que recibe» de TNV, como «Correos que recibe» (#532). Por defecto, encendidas. NO dependen de
    /// RecibirConsejos: es otro interruptor. Los avisos de pedidos, cheques, inscripciones y recordatorios de eventos son
    /// de servicio y no se pueden quitar.
    /// </summary>
    public static class TiposNotificacionCliente
    {
        /// <summary>El anuncio de un evento nuevo (POST api/Eventos/{id}/Anunciar).</summary>
        public const string EVENTOS_Y_CURSOS = "EventosYCursos";
        /// <summary>La push de reposición de los lunes a jueves (RecordatorioReposicionPushJobsService, #662).</summary>
        public const string AVISOS_REPOSICION = "AvisosReposicion";

        public static readonly IReadOnlyList<TipoNotificacionCliente> Todos = new List<TipoNotificacionCliente>
        {
            new TipoNotificacionCliente(EVENTOS_Y_CURSOS, "Eventos y cursos",
                "Le avisamos cuando organizamos un curso, una masterclass o un evento al que puede apuntarse. Los presenciales, " +
                "solo si está cerca de nuestras tiendas de Madrid."),
            new TipoNotificacionCliente(AVISOS_REPOSICION, "Avisos de reposición",
                "Cuando, por cómo suele comprarlo, puede estar a punto de quedarse sin un producto, le avisamos en el móvil por si " +
                "le viene bien tenerlo a mano. Como mucho, una vez por semana.")
        };

        /// <summary>El tipo con ese código, sin distinguir mayúsculas; null si no existe.</summary>
        public static TipoNotificacionCliente Buscar(string tipo)
            => string.IsNullOrWhiteSpace(tipo)
                ? null
                : Todos.FirstOrDefault(t => string.Equals(t.Tipo, tipo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>NestoAPI#692: lo que ve la app de un tipo de notificación (GET/PUT api/Clientes/PreferenciasNotificaciones).</summary>
    public class PreferenciaNotificacionDTO
    {
        public string Tipo { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        /// <summary>True = la recibe; false = la ha apagado.</summary>
        public bool Activo { get; set; }
    }

    /// <summary>NestoAPI#692: cuerpo del PUT api/Clientes/PreferenciasNotificaciones/{tipo}.</summary>
    public class PreferenciaNotificacionRequest
    {
        public bool? Activo { get; set; }
    }

    /// <summary>NestoAPI#692: las bajas de notificaciones (dbo.NotificacionesClientesBajas, SQL a mano como #532).</summary>
    public interface IRepositorioPreferenciasNotificaciones
    {
        /// <summary>Los tipos que ese cliente ha apagado.</summary>
        Task<ISet<string>> LeerBajas(string empresa, string cliente);
        Task DarDeBaja(string empresa, string cliente, string tipo, string usuario);
        Task QuitarBaja(string empresa, string cliente, string tipo);
        /// <summary>Los clientes (sin espacios) que han apagado ese tipo.</summary>
        Task<ISet<string>> ClientesDeBaja(string empresa, string tipo);
    }

    public class RepositorioPreferenciasNotificacionesSql : IRepositorioPreferenciasNotificaciones
    {
        internal const string SQL_LEER = @"
SELECT RTRIM(Tipo) FROM dbo.NotificacionesClientesBajas WITH (NOLOCK) WHERE Empresa = @empresa AND Cliente = @cliente";

        internal const string SQL_DAR_DE_BAJA = @"
IF NOT EXISTS (SELECT 1 FROM dbo.NotificacionesClientesBajas WHERE Empresa = @empresa AND Cliente = @cliente AND Tipo = @tipo)
    INSERT INTO dbo.NotificacionesClientesBajas (Empresa, Cliente, Tipo, Usuario) VALUES (@empresa, @cliente, @tipo, @usuario)";

        internal const string SQL_QUITAR_BAJA = @"
DELETE FROM dbo.NotificacionesClientesBajas WHERE Empresa = @empresa AND Cliente = @cliente AND Tipo = @tipo";

        internal const string SQL_CLIENTES_DE_BAJA = @"
SELECT DISTINCT RTRIM(Cliente) FROM dbo.NotificacionesClientesBajas WITH (NOLOCK) WHERE Empresa = @empresa AND Tipo = @tipo";

        private readonly NVEntities db;

        public RepositorioPreferenciasNotificacionesSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<ISet<string>> LeerBajas(string empresa, string cliente)
        {
            List<string> tipos = await db.Database.SqlQuery<string>(SQL_LEER,
                new SqlParameter("@empresa", empresa), new SqlParameter("@cliente", cliente.Trim()))
                .ToListAsync().ConfigureAwait(false);
            return new HashSet<string>(tipos.Where(t => t != null).Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        public async Task DarDeBaja(string empresa, string cliente, string tipo, string usuario)
            => _ = await db.Database.ExecuteSqlCommandAsync(SQL_DAR_DE_BAJA,
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@cliente", cliente.Trim()),
                new SqlParameter("@tipo", tipo),
                new SqlParameter("@usuario", string.IsNullOrWhiteSpace(usuario) ? (object)DBNull.Value
                    : (usuario.Length > 50 ? usuario.Substring(0, 50) : usuario))).ConfigureAwait(false);

        public async Task QuitarBaja(string empresa, string cliente, string tipo)
            => _ = await db.Database.ExecuteSqlCommandAsync(SQL_QUITAR_BAJA,
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@cliente", cliente.Trim()),
                new SqlParameter("@tipo", tipo)).ConfigureAwait(false);

        public async Task<ISet<string>> ClientesDeBaja(string empresa, string tipo)
        {
            List<string> clientes = await db.Database.SqlQuery<string>(SQL_CLIENTES_DE_BAJA,
                new SqlParameter("@empresa", empresa), new SqlParameter("@tipo", tipo))
                .ToListAsync().ConfigureAwait(false);
            return new HashSet<string>(clientes.Where(c => c != null).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>NestoAPI#692: qué notificaciones recibe un cliente de TNV y cómo cambiarlo. Sin fila = la recibe.</summary>
    public class ServicioPreferenciasNotificaciones
    {
        private readonly IRepositorioPreferenciasNotificaciones repositorio;

        public ServicioPreferenciasNotificaciones(IRepositorioPreferenciasNotificaciones repositorio)
        {
            this.repositorio = repositorio;
        }

        public async Task<List<PreferenciaNotificacionDTO>> Leer(string empresa, string cliente)
        {
            ISet<string> bajas = await repositorio.LeerBajas(empresa, cliente).ConfigureAwait(false)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return TiposNotificacionCliente.Todos.Select(t => new PreferenciaNotificacionDTO
            {
                Tipo = t.Tipo,
                Nombre = t.Nombre,
                Descripcion = t.Descripcion,
                Activo = !bajas.Contains(t.Tipo)
            }).ToList();
        }

        /// <summary>Enciende o apaga un tipo. Null si el tipo no existe (no se toca nada).</summary>
        public async Task<List<PreferenciaNotificacionDTO>> Cambiar(string empresa, string cliente, string tipo, bool activo, string usuario)
        {
            TipoNotificacionCliente tipoNotificacion = TiposNotificacionCliente.Buscar(tipo);
            if (tipoNotificacion == null)
            {
                return null;
            }
            if (activo)
            {
                await repositorio.QuitarBaja(empresa, cliente, tipoNotificacion.Tipo).ConfigureAwait(false);
            }
            else
            {
                await repositorio.DarDeBaja(empresa, cliente, tipoNotificacion.Tipo, usuario).ConfigureAwait(false);
            }
            return await Leer(empresa, cliente).ConfigureAwait(false);
        }

        /// <summary>
        /// Para los envíos: los clientes que han apagado ese tipo. Si no se puede leer (la tabla aún no existe porque no se ha
        /// lanzado Scripts/Issue692_AnunciarEventos.sql), se loguea y se manda a todos, como antes de existir el interruptor.
        /// </summary>
        public static async Task<ISet<string>> ClientesDeBaja(IRepositorioPreferenciasNotificaciones repositorio, string empresa, string tipo)
        {
            try
            {
                return await repositorio.ClientesDeBaja(empresa, tipo).ConfigureAwait(false)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Notificaciones que recibe #692] No se han podido leer las bajas de {tipo}; se manda a todos: {ex.Message}", ex));
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
