using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>Qué hacer con una notificación de Redsys según el estado del enlace antes de llegar.</summary>
    public enum DecisionNotificacionAnulado
    {
        /// <summary>El enlace no estaba anulado: como siempre.</summary>
        ComoSiempre,
        /// <summary>
        /// Anulado y el cliente lo ha pagado igualmente (tenía la página de Redsys abierta antes de anularlo): el dinero ya está
        /// cobrado, así que se apunta como cualquier cobro (Autorizado, contabilizado, correo post-cobro) y además se avisa.
        /// </summary>
        CobrarYAvisar,
        /// <summary>Anulado y denegado: sigue anulado y NO se regenera otro enlace (#156).</summary>
        DejarAnulado
    }

    /// <summary>
    /// Anular enlaces de pago (PagosTPV de tipo TPV virtual que siguen pendientes). Un solo UPDATE para la consulta de reembolso
    /// (NestoAPI#734, «Anular el enlace pendiente») y para el botón «Anular enlace» de Administración (Contabilidad › Cobros ›
    /// Enlaces de pago, 09/11/26). Solo TPV virtual: los P2F («Enviado», ServicioReclamacionDeuda) son enlaces de Redsys que no
    /// pasan por nuestra página de pago y anularlos aquí no impediría pagarlos; los de la app y Aplázame tienen su propio circuito.
    /// </summary>
    public static class AnulacionEnlacesPago
    {
        // Solo los enlaces de pago de verdad (TPV virtual) que siguen pendientes: uno cobrado entretanto no se toca. OUTPUT INTO
        // una variable (y no OUTPUT a secas) para que no falle si algún día PagosTPV tiene triggers.
        internal const string SQL_ANULAR = "DECLARE @anulados TABLE (Id int); " +
            "UPDATE dbo.PagosTPV SET Estado = @cancelado, FechaActualizacion = GETDATE() OUTPUT inserted.Id INTO @anulados " +
            "WHERE Empresa = @empresa AND Tipo = @tipo AND Estado = @pendiente AND Id IN ({0}); SELECT Id FROM @anulados ORDER BY Id";

        internal const string SQL_MODIFICACION = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES ('PagosTPV', @anterior, @nuevo, @usuario)";

        /// <summary>Anula esos enlaces si siguen pendientes y son de TPV virtual. Devuelve los que ha anulado.</summary>
        public static async Task<List<int>> Anular(NVEntities db, string empresa, IEnumerable<int> ids)
        {
            List<int> lista = (ids ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().ToList();
            if (!lista.Any())
            {
                return new List<int>();
            }
            // Enteros: van en el texto sin riesgo de inyección.
            string sql = string.Format(CultureInfo.InvariantCulture, SQL_ANULAR, string.Join(", ", lista));
            return await db.Database.SqlQuery<int>(sql,
                new SqlParameter("@cancelado", SqlDbType.NVarChar, 20) { Value = Constantes.EstadosPagoTPV.CANCELADO },
                // PagosTPV.Empresa es nvarchar(2), no char(3) como CabPedidoVta.
                new SqlParameter("@empresa", SqlDbType.NVarChar, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() },
                new SqlParameter("@tipo", SqlDbType.NVarChar, 20) { Value = Constantes.TiposPagoTPV.TPV_VIRTUAL },
                new SqlParameter("@pendiente", SqlDbType.NVarChar, 20) { Value = Constantes.EstadosPagoTPV.PENDIENTE })
                .ToListAsync().ConfigureAwait(false);
        }

        /// <summary>Lo que queda en Modificaciones (Tabla = 'PagosTPV') al anular a mano.</summary>
        public static (string anterior, string nuevo) TextosModificacion(EnlaceAAnular enlace, string motivo)
        {
            string anterior = string.Format(CultureInfo.InvariantCulture, "PagosTPV {0} ({1}) Estado={2} Cliente={3} Importe={4:0.00} Concepto={5}",
                enlace.Id, enlace.NumeroOrden?.Trim(), enlace.Estado?.Trim(), enlace.Cliente?.Trim(), enlace.Importe, enlace.Descripcion?.Trim());
            string nuevo = $"Estado={Constantes.EstadosPagoTPV.CANCELADO} (anulado a mano). Motivo: {motivo}";
            return (anterior, nuevo);
        }

        /// <summary>¿Llega una notificación de Redsys de un enlace que habíamos anulado?</summary>
        public static DecisionNotificacionAnulado DecidirNotificacion(string estadoAnterior, string tipo, bool pagoAutorizado)
        {
            bool anulado = string.Equals(tipo?.Trim(), Constantes.TiposPagoTPV.TPV_VIRTUAL, StringComparison.OrdinalIgnoreCase)
                && EnlacesPagoPedido.EstaAnulado(estadoAnterior);
            if (!anulado)
            {
                return DecisionNotificacionAnulado.ComoSiempre;
            }
            return pagoAutorizado ? DecisionNotificacionAnulado.CobrarYAvisar : DecisionNotificacionAnulado.DejarAnulado;
        }
    }

    /// <summary>Lo que hace falta del enlace para anularlo y dejar constancia.</summary>
    public class EnlaceAAnular
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Tipo { get; set; }
        public string Estado { get; set; }
        public string Cliente { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }
        public string NumeroOrden { get; set; }
    }

    public interface IRepositorioAnulacionEnlacesPago
    {
        /// <summary>El enlace, o null si no existe.</summary>
        Task<EnlaceAAnular> Leer(int id);

        /// <summary>
        /// Lo anula con <see cref="AnulacionEnlacesPago.SQL_ANULAR"/> (solo si sigue pendiente y es TPV virtual) y apunta en
        /// Modificaciones quién y por qué, en la misma transacción. Devuelve la FechaActualizacion, o null si no lo ha anulado.
        /// </summary>
        Task<DateTime?> Anular(EnlaceAAnular enlace, string usuario, string motivo);
    }

    public class RepositorioAnulacionEnlacesPago : IRepositorioAnulacionEnlacesPago
    {
        private static NVEntities Contexto()
        {
            var db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
        }

        public async Task<EnlaceAAnular> Leer(int id)
        {
            using (NVEntities db = Contexto())
            {
                return await db.PagosTPV.AsNoTracking()
                    .Where(p => p.Id == id)
                    .Select(p => new EnlaceAAnular
                    {
                        Id = p.Id, Empresa = p.Empresa, Tipo = p.Tipo, Estado = p.Estado, Cliente = p.Cliente, Importe = p.Importe,
                        Descripcion = p.Descripcion, NumeroOrden = p.NumeroOrden
                    })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<DateTime?> Anular(EnlaceAAnular enlace, string usuario, string motivo)
        {
            using (NVEntities db = Contexto())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                List<int> anulados = await AnulacionEnlacesPago.Anular(db, enlace.Empresa, new[] { enlace.Id }).ConfigureAwait(false);
                if (!anulados.Contains(enlace.Id))
                {
                    transaccion.Rollback();
                    return null;
                }
                (string anterior, string nuevo) = AnulacionEnlacesPago.TextosModificacion(enlace, motivo);
                // Con el usuario de verdad (como #698/#734): con EF, Usuario caería en su DEFAULT suser_sname().
                _ = await db.Database.ExecuteSqlCommandAsync(AnulacionEnlacesPago.SQL_MODIFICACION,
                    new SqlParameter("@anterior", SqlDbType.NVarChar, -1) { Value = anterior },
                    new SqlParameter("@nuevo", SqlDbType.NVarChar, -1) { Value = nuevo },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 100) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) }).ConfigureAwait(false);
                DateTime? fecha = await db.PagosTPV.AsNoTracking().Where(p => p.Id == enlace.Id)
                    .Select(p => p.FechaActualizacion).FirstOrDefaultAsync().ConfigureAwait(false);
                transaccion.Commit();
                return fecha ?? DateTime.Now;
            }
        }
    }

    /// <summary>Lo que devuelve el servicio al controlador (que solo traduce a 200/400/404/409).</summary>
    public class ResultadoAnulacionEnlace
    {
        public bool NoEncontrado { get; set; }
        public bool Conflicto { get; set; }
        public string Error { get; set; }
        public RespuestaAnularEnlacePago Respuesta { get; set; }

        public static ResultadoAnulacionEnlace Fallo(string error) => new ResultadoAnulacionEnlace { Error = error };
        public static ResultadoAnulacionEnlace EnConflicto(string error) => new ResultadoAnulacionEnlace { Conflicto = true, Error = error };
        public static ResultadoAnulacionEnlace SinEnlace() => new ResultadoAnulacionEnlace { NoEncontrado = true };
        public static ResultadoAnulacionEnlace Exito(RespuestaAnularEnlacePago respuesta) => new ResultadoAnulacionEnlace { Respuesta = respuesta };
    }

    public interface IServicioAnulacionEnlacesPago
    {
        Task<ResultadoAnulacionEnlace> Anular(int id, string motivo, string usuario);
    }

    /// <summary>
    /// Carlos (09/11/26, sugerencia 1140 de Alfredo): Administración anula ella misma un enlace de pago pendiente. El 867 (pedido
    /// 928286, ya cobrado por reembolso con CTT) seguía Pendiente y bloqueaba la etiqueta del 930167 por el aviso de NestoAPI#824.
    /// </summary>
    public class ServicioAnulacionEnlacesPago : IServicioAnulacionEnlacesPago
    {
        public const int LONGITUD_MAXIMA_MOTIVO = 200;

        private readonly IRepositorioAnulacionEnlacesPago repositorio;

        public ServicioAnulacionEnlacesPago(IRepositorioAnulacionEnlacesPago repositorio = null)
        {
            this.repositorio = repositorio ?? new RepositorioAnulacionEnlacesPago();
        }

        public async Task<ResultadoAnulacionEnlace> Anular(int id, string motivo, string usuario)
        {
            string limpio = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            if (limpio == null)
            {
                return ResultadoAnulacionEnlace.Fallo("Indique el motivo por el que anula el enlace (por ejemplo, «Pedido ya cobrado por reembolso»).");
            }
            if (limpio.Length > LONGITUD_MAXIMA_MOTIVO)
            {
                return ResultadoAnulacionEnlace.Fallo($"El motivo no puede pasar de {LONGITUD_MAXIMA_MOTIVO} caracteres.");
            }

            EnlaceAAnular enlace = await repositorio.Leer(id).ConfigureAwait(false);
            if (enlace == null)
            {
                return ResultadoAnulacionEnlace.SinEnlace();
            }
            string conflicto = PorQueNoSePuedeAnular(enlace);
            if (conflicto != null)
            {
                return ResultadoAnulacionEnlace.EnConflicto(conflicto);
            }

            DateTime? fecha = await repositorio.Anular(enlace, usuario, limpio).ConfigureAwait(false);
            if (!fecha.HasValue)
            {
                // Entre leerlo y anularlo ha cambiado (lo ha pagado el cliente, o lo ha anulado otra persona).
                EnlaceAAnular ahora = await repositorio.Leer(id).ConfigureAwait(false);
                return ResultadoAnulacionEnlace.EnConflicto($"El enlace {id} ya no está pendiente (ahora está {ahora?.Estado?.Trim() ?? "borrado"}): " +
                    "no se ha anulado. Vuelva a cargar la lista.");
            }
            return ResultadoAnulacionEnlace.Exito(new RespuestaAnularEnlacePago
            {
                Id = id,
                Estado = Constantes.EstadosPagoTPV.CANCELADO,
                FechaActualizacion = fecha.Value,
                Mensaje = string.Format(new CultureInfo("es-ES"), "Enlace {0} de {1:C} anulado: el cliente ya no puede pagarlo.", id, enlace.Importe)
            });
        }

        /// <summary>Null si se puede anular; si no, el motivo para el 409.</summary>
        internal static string PorQueNoSePuedeAnular(EnlaceAAnular enlace)
        {
            if (!string.Equals(enlace.Tipo?.Trim(), Constantes.TiposPagoTPV.TPV_VIRTUAL, StringComparison.OrdinalIgnoreCase))
            {
                return $"El enlace {enlace.Id} es de tipo {enlace.Tipo?.Trim()}: solo se pueden anular los enlaces de pago (TPV virtual).";
            }
            string estado = enlace.Estado?.Trim();
            if (string.Equals(estado, Constantes.EstadosPagoTPV.PENDIENTE, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (string.Equals(estado, Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase))
            {
                return $"El enlace {enlace.Id} ya está pagado: no se puede anular. Si hay que devolver el dinero, hágalo desde el TPV.";
            }
            if (EnlacesPagoPedido.EstaAnulado(estado))
            {
                return $"El enlace {enlace.Id} ya está anulado.";
            }
            if (string.Equals(estado, Constantes.EstadosPagoTPV.DENEGADO, StringComparison.OrdinalIgnoreCase))
            {
                return $"El enlace {enlace.Id} está denegado: el cliente no lo pagó y no hace falta anularlo.";
            }
            return $"El enlace {enlace.Id} está {estado}: solo se pueden anular los pendientes.";
        }
    }
}
