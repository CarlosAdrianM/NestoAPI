using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#699 (corte 2): las decisiones sobre un envío devuelto o incidentado.</summary>
    public static class DecisionesReenvio
    {
        /// <summary>Nadie ha dicho aún si el cliente lo sigue queriendo (no hay fila en la tabla).</summary>
        public const string SIN_DECIDIR = "SinDecidir";
        /// <summary>El cliente lo sigue queriendo: se ha creado un envío nuevo del mismo pedido.</summary>
        public const string REENVIAR = "Reenviar";
        /// <summary>El cliente no lo quiere: el almacén lo abona con Copiar factura.</summary>
        public const string ABONAR = "Abonar";
        /// <summary>
        /// Se decidió reenviar, pero la agencia acabó entregando el envío original: el reenvío, que aún no tenía etiqueta, se
        /// ha borrado (su número queda en EnvioAnulado). Lo pone el seguimiento, no una persona.
        /// </summary>
        public const string ENTREGADO_POR_LA_AGENCIA = "EntregadoPorLaAgencia";
    }

    /// <summary>NestoAPI#699: una fila de EnviosAgenciaReenvios (fuera del EDMX).</summary>
    public class RegistroReenvioEnvio
    {
        public int Id { get; set; }
        public int EnvioOrigen { get; set; }
        public string Decision { get; set; }
        public int? EnvioNuevo { get; set; }
        /// <summary>Con <see cref="DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA"/>: el número del reenvío que se borró (sin FK).</summary>
        public int? EnvioAnulado { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string Observaciones { get; set; }
        public bool IncluirPortes { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#699: qué pasó al intentar anular el reenvío de un envío que la agencia acabó entregando.</summary>
    public enum ResultadoAnulacionReenvio
    {
        /// <summary>Borrado el envío nuevo y la fila pasada a EntregadoPorLaAgencia, con su historia en el viejo.</summary>
        Anulado = 0,
        /// <summary>El envío nuevo ya está tramitado (o ya no existe en curso): no se ha tocado nada.</summary>
        YaTramitado = 1,
        /// <summary>La fila ya no es un Reenviar con ese envío nuevo (otra pasada se ha adelantado): nada que hacer.</summary>
        SinReenvioPendiente = 2
    }

    /// <summary>NestoAPI#699: POST api/EnviosAgencias/{id}/Reenviar. Lo que no venga (null) se copia del envío devuelto.</summary>
    public class ReenviarEnvioDTO
    {
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string Observaciones { get; set; }
        /// <summary>Carlos 23/10/26: los portes del reenvío «nos los comemos» por defecto; true añade una línea de portes al pedido.</summary>
        public bool IncluirPortes { get; set; }
    }

    /// <summary>NestoAPI#699: la respuesta de Reenviar y de Abonar.</summary>
    public class DecisionReenvioDTO
    {
        public int EnvioOrigen { get; set; }
        public int? Pedido { get; set; }
        public string Decision { get; set; }
        public int? EnvioNuevo { get; set; }
        /// <summary>El código de barras del envío nuevo si la agencia lo calcula al grabar (GLS); vacío si lo da al tramitar.</summary>
        public string CodigoBarras { get; set; }
        public bool PortesIncluidos { get; set; }
        public decimal Reembolso { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#699: GET api/EnviosAgencias/Devueltos. El listado de siempre con la decisión.</summary>
    public class EnvioDevueltoDTO : EnvioAgenciaListadoDTO
    {
        /// <summary><see cref="DecisionesReenvio"/>: SinDecidir, Reenviar, Abonar o EntregadoPorLaAgencia.</summary>
        public string Decision { get; set; }
        public int? EnvioNuevo { get; set; }
        /// <summary>Con EntregadoPorLaAgencia: el reenvío que se borró porque la agencia acabó entregando el original.</summary>
        public int? EnvioAnulado { get; set; }
        /// <summary>El estado del envío nuevo (0 en curso, 1 tramitado...).</summary>
        public short? EstadoEnvioNuevo { get; set; }
        /// <summary>El envío nuevo ya está tramitado con la agencia (tiene su etiqueta).</summary>
        public bool EnvioNuevoConEtiqueta { get; set; }
        public bool PortesIncluidos { get; set; }
        public string UsuarioDecision { get; set; }
        public DateTime? FechaDecision { get; set; }
    }

    /// <summary>
    /// NestoAPI#699: acceso a EnviosAgenciaReenvios, por SQL y comprobando que existe (patrón de
    /// <see cref="RegistroReembolsosAlEntregar"/>). Sin el script, <see cref="TablaDisponible"/> es false y los endpoints dan 400.
    /// </summary>
    public interface IRepositorioReenviosEnvio
    {
        bool TablaDisponible();
        Task<RegistroReenvioEnvio> LeerAsync(int envioOrigen);
        Task<Dictionary<int, RegistroReenvioEnvio>> LeerAsync(IEnumerable<int> enviosOrigen);
        /// <summary>Inserta la decisión si el envío no tenía ninguna. False si ya la tenía (otra sesión se ha adelantado).</summary>
        Task<bool> InsertarAsync(RegistroReenvioEnvio registro);
        /// <summary>Los envíos de origen con esa decisión (para enseñar en Devueltos los que la agencia acabó entregando).</summary>
        Task<List<int>> LeerOrigenesConDecisionAsync(string decision);
        /// <summary>
        /// En una transacción: si la fila sigue siendo Reenviar con ese envío nuevo y el envío nuevo sigue sin tramitar
        /// (Estado &lt; 1), la pasa a EntregadoPorLaAgencia (EnvioAnulado = el nuevo), borra el envío nuevo con su historia y
        /// deja constancia en la historia del viejo. Si no, no toca nada.
        /// </summary>
        Task<ResultadoAnulacionReenvio> AnularPorEntregaAsync(int envioOrigen, int envioNuevo, string usuario, string observaciones);
    }

    public class RepositorioReenviosEnvio : IRepositorioReenviosEnvio
    {
        public const string TABLA = "EnviosAgenciaReenvios";
        private const string COLUMNAS = "Id, EnvioOrigen, Decision, EnvioNuevo, EnvioAnulado, Direccion, CodPostal, Poblacion, Provincia, Telefono, Movil, " +
            "Observaciones, IncluirPortes, Usuario, Fecha";

        private readonly NVEntities db;

        public RepositorioReenviosEnvio(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool TablaDisponible() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public async Task<RegistroReenvioEnvio> LeerAsync(int envioOrigen)
        {
            if (!TablaDisponible())
            {
                return null;
            }
            List<RegistroReenvioEnvio> filas = await db.Database.SqlQuery<RegistroReenvioEnvio>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE EnvioOrigen = @p0", envioOrigen).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public async Task<Dictionary<int, RegistroReenvioEnvio>> LeerAsync(IEnumerable<int> enviosOrigen)
        {
            List<int> numeros = (enviosOrigen ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (numeros.Count == 0 || !TablaDisponible())
            {
                return new Dictionary<int, RegistroReenvioEnvio>();
            }
            // Son enteros: van en el IN sin riesgo de inyección. Pocos (los devueltos de 60 días).
            string lista = string.Join(",", numeros.Select(n => n.ToString(CultureInfo.InvariantCulture)));
            List<RegistroReenvioEnvio> filas = await db.Database.SqlQuery<RegistroReenvioEnvio>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE EnvioOrigen IN ({lista})").ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(f => f.EnvioOrigen).ToDictionary(g => g.Key, g => g.First());
        }

        public async Task<bool> InsertarAsync(RegistroReenvioEnvio r)
        {
            int filas = await db.Database.ExecuteSqlCommandAsync(
                $"IF NOT EXISTS (SELECT 1 FROM dbo.{TABLA} WITH (UPDLOCK, HOLDLOCK) WHERE EnvioOrigen = @p0) " +
                $"INSERT INTO dbo.{TABLA} (EnvioOrigen, Decision, EnvioNuevo, Direccion, CodPostal, Poblacion, Provincia, Telefono, Movil, " +
                "Observaciones, IncluirPortes, Usuario, Fecha) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, GETDATE())",
                r.EnvioOrigen, r.Decision, (object)r.EnvioNuevo ?? DBNull.Value,
                Nulo(r.Direccion, 100), Nulo(r.CodPostal, 20), Nulo(r.Poblacion, 100), Nulo(r.Provincia, 100),
                Nulo(r.Telefono, 50), Nulo(r.Movil, 50), Nulo(r.Observaciones, 255), r.IncluirPortes,
                Nulo(string.IsNullOrWhiteSpace(r.Usuario) ? "NestoAPI" : r.Usuario.Trim(), 50)).ConfigureAwait(false);
            return filas == 1;
        }

        public async Task<List<int>> LeerOrigenesConDecisionAsync(string decision)
        {
            if (string.IsNullOrWhiteSpace(decision) || !TablaDisponible())
            {
                return new List<int>();
            }
            return await db.Database.SqlQuery<int>($"SELECT EnvioOrigen FROM dbo.{TABLA} WHERE Decision = @p0", decision)
                .ToListAsync().ConfigureAwait(false);
        }

        /// <summary>El Campo de la fila de EnviosHistoria que deja la anulación en el envío viejo (ValorAnterior = el reenvío).</summary>
        public const string CAMPO_HISTORIA = "Reenvio";

        public async Task<ResultadoAnulacionReenvio> AnularPorEntregaAsync(int envioOrigen, int envioNuevo, string usuario, string observaciones)
        {
            // Todo o nada, en un solo lote: la FK de EnvioNuevo obliga a soltarlo antes de borrar el envío, y el borrado solo
            // vale si el envío nuevo sigue sin etiqueta (Estado < 1) en ese momento.
            string sql =
                "SET XACT_ABORT ON; BEGIN TRANSACTION; " +
                $"IF NOT EXISTS (SELECT 1 FROM dbo.{TABLA} WITH (UPDLOCK, HOLDLOCK) WHERE EnvioOrigen = @p0 AND Decision = @p2 AND EnvioNuevo = @p1) " +
                $"BEGIN ROLLBACK TRANSACTION; SELECT {(int)ResultadoAnulacionReenvio.SinReenvioPendiente}; RETURN; END; " +
                $"IF NOT EXISTS (SELECT 1 FROM dbo.EnviosAgencia WITH (UPDLOCK, HOLDLOCK) WHERE Numero = @p1 AND Estado < {Constantes.Agencias.ESTADO_TRAMITADO}) " +
                $"BEGIN ROLLBACK TRANSACTION; SELECT {(int)ResultadoAnulacionReenvio.YaTramitado}; RETURN; END; " +
                $"UPDATE dbo.{TABLA} SET Decision = @p3, EnvioAnulado = @p1, EnvioNuevo = NULL WHERE EnvioOrigen = @p0; " +
                "DELETE FROM dbo.EnviosHistoria WHERE NumeroEnvio = @p1; " +
                "DELETE FROM dbo.EnviosAgencia WHERE Numero = @p1; " +
                "INSERT INTO dbo.EnviosHistoria (NumeroEnvio, Campo, ValorAnterior, Observaciones, Usuario, FechaModificacion) " +
                "VALUES (@p0, @p4, @p5, @p6, @p7, GETDATE()); " +
                "COMMIT TRANSACTION; " +
                $"SELECT {(int)ResultadoAnulacionReenvio.Anulado};";
            List<int> resultado = await db.Database.SqlQuery<int>(sql,
                envioOrigen, envioNuevo, DecisionesReenvio.REENVIAR, DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA,
                CAMPO_HISTORIA, envioNuevo.ToString(CultureInfo.InvariantCulture), observaciones ?? string.Empty,
                Nulo(string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim(), 50)).ToListAsync().ConfigureAwait(false);
            return resultado.Any() ? (ResultadoAnulacionReenvio)resultado.First() : ResultadoAnulacionReenvio.SinReenvioPendiente;
        }

        private static object Nulo(string texto, int longitud)
        {
            if (texto == null)
            {
                return DBNull.Value;
            }
            string limpio = texto.Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }

    /// <summary>NestoAPI#699: lo que el servicio de reenvíos necesita de la BD (sustituible en los tests).</summary>
    public interface IDatosReenviosEnvio
    {
        IRepositorioReenviosEnvio Reenvios { get; }
        Task<EnviosAgencia> LeerEnvioAsync(int numero);
        Task<AgenciaTransporte> LeerAgenciaAsync(int agencia);
        /// <summary>El reembolso que el seguimiento le quitó al envío al pasar a devuelto (Sugerencia 544), o null.</summary>
        Task<decimal?> ReembolsoQuitadoPorDevueltoAsync(int envio);
        Task<List<EnvioAgenciaListadoDTO>> LeerDevueltosEIncidentadosAsync(string empresa, DateTime desde);
        Task<Dictionary<int, short>> LeerEstadosAsync(IEnumerable<int> envios);
        void Anadir(EnviosAgencia envio);
        Task GuardarCambios();
        Task<T> EnTransaccion<T>(Func<Task<T>> cuerpo);
        /// <summary>Añade al pedido la línea de portes del reenvío (pendiente de facturar). Devuelve el importe.</summary>
        decimal AnadirLineaPortes(EnviosAgencia envio, string usuario);
    }

    public enum TipoResultadoReenvio
    {
        Correcto,
        NoEncontrado,
        /// <summary>400: falta el script, o datos incorrectos.</summary>
        Invalido,
        /// <summary>409: ya decidido, entregado o en un estado en el que no cabe.</summary>
        Conflicto
    }

    public class ResultadoReenvio
    {
        public TipoResultadoReenvio Tipo { get; set; }
        public string Mensaje { get; set; }
        public DecisionReenvioDTO Decision { get; set; }

        public static ResultadoReenvio Correcto(DecisionReenvioDTO d) => new ResultadoReenvio { Tipo = TipoResultadoReenvio.Correcto, Decision = d, Mensaje = d?.Mensaje };
        public static ResultadoReenvio NoEncontrado(int id) => new ResultadoReenvio { Tipo = TipoResultadoReenvio.NoEncontrado, Mensaje = $"No existe el envío {id}." };
        public static ResultadoReenvio Invalido(string m) => new ResultadoReenvio { Tipo = TipoResultadoReenvio.Invalido, Mensaje = m };
        public static ResultadoReenvio Conflicto(string m) => new ResultadoReenvio { Tipo = TipoResultadoReenvio.Conflicto, Mensaje = m };
    }

    public interface IServicioReenviosEnvio
    {
        Task<ResultadoReenvio> ReenviarAsync(int envioOrigen, ReenviarEnvioDTO datos, string usuario);
        Task<ResultadoReenvio> AbonarAsync(int envioOrigen, string usuario);
        /// <summary>Null si no está la tabla.</summary>
        Task<List<EnvioDevueltoDTO>> LeerDevueltosAsync(string empresa);
    }

    /// <summary>
    /// NestoAPI#699 (corte 2, «Volver a enviar»): la agencia devuelve un envío (o lo deja incidentado) y alguien (almacén o
    /// vendedor) decide si el cliente lo sigue queriendo.
    /// <list type="bullet">
    /// <item><b>Reenviar</b>: un envío NUEVO del mismo pedido, clonado del devuelto (dirección y teléfonos corregibles), en curso
    /// y sin etiqueta: el almacén lo tramita e imprime como cualquier otro cuando llega la caja. Sin abono, sin stock, sin
    /// picking ni factura. Portes solo si se pide (por defecto, no).</item>
    /// <item><b>Abonar</b>: solo queda la decisión; el abono lo hace el almacén con Copiar factura, como hoy.</item>
    /// </list>
    /// Cada envío tiene como mucho UNA decisión (EnviosAgenciaReenvios, fuera del EDMX): es la constancia y la estadística.
    /// </summary>
    public class ServicioReenviosEnvio : IServicioReenviosEnvio
    {
        /// <summary>Los devueltos e incidentados del listado: los de los últimos N días.</summary>
        public const int DIAS_LISTADO = 60;

        internal const string MENSAJE_SIN_TABLA = "Falta lanzar el script Scripts/Issue699_EnviosAgenciaReenvios.sql: " +
            "todavía no se pueden registrar reenvíos.";

        private readonly IDatosReenviosEnvio datos;
        private readonly RegistroPlazasAgencias plazas;
        private readonly Func<DateTime> hoy;

        public ServicioReenviosEnvio(IDatosReenviosEnvio datos, RegistroPlazasAgencias plazas, Func<DateTime> hoy = null)
        {
            this.datos = datos ?? throw new ArgumentNullException(nameof(datos));
            this.plazas = plazas ?? throw new ArgumentNullException(nameof(plazas));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        /// <summary>Solo los devueltos (4) y los incidentados (3) admiten decisión.</summary>
        internal static bool AdmiteDecision(EnviosAgencia envio) =>
            envio != null && (envio.Estado == Constantes.Agencias.ESTADO_DEVUELTO || envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO);

        private async Task<ResultadoReenvio> Comprobar(int id, EnviosAgencia envio)
        {
            if (envio == null)
            {
                return ResultadoReenvio.NoEncontrado(id);
            }
            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return ResultadoReenvio.Conflicto($"El envío {id} ya está entregado: no hay nada que reenviar ni abonar.");
            }
            if (!AdmiteDecision(envio))
            {
                return ResultadoReenvio.Conflicto($"El envío {id} no está devuelto ni incidentado: solo se puede volver a enviar o " +
                    "abonar un envío que la agencia ha devuelto o tiene con incidencia.");
            }
            RegistroReenvioEnvio anterior = await datos.Reenvios.LeerAsync(id);
            if (anterior != null)
            {
                return ResultadoReenvio.Conflicto(TextoYaDecidido(id, anterior));
            }
            return null;
        }

        internal static string TextoYaDecidido(int id, RegistroReenvioEnvio anterior)
        {
            string quien = string.IsNullOrWhiteSpace(anterior.Usuario) ? string.Empty : $" ({anterior.Usuario.Trim()}, {anterior.Fecha:dd/MM/yyyy HH:mm})";
            return anterior.Decision == DecisionesReenvio.REENVIAR
                ? $"El envío {id} ya se volvió a enviar en el envío {anterior.EnvioNuevo}{quien}."
                : $"El envío {id} ya está marcado para abonar{quien}. Si el cliente lo quiere, rehaga el pedido con Copiar factura " +
                  "(«El cliente lo sigue queriendo: rehacer el pedido»).";
        }

        public async Task<ResultadoReenvio> ReenviarAsync(int id, ReenviarEnvioDTO peticion, string usuario)
        {
            if (!datos.Reenvios.TablaDisponible())
            {
                return ResultadoReenvio.Invalido(MENSAJE_SIN_TABLA);
            }
            peticion = peticion ?? new ReenviarEnvioDTO();
            EnviosAgencia origen = await datos.LeerEnvioAsync(id);
            ResultadoReenvio error = await Comprobar(id, origen);
            if (error != null)
            {
                return error;
            }

            var avisos = new List<string>();
            decimal reembolso = origen.Reembolso;
            if (reembolso <= 0m && origen.Estado == Constantes.Agencias.ESTADO_DEVUELTO)
            {
                // Sugerencia 544: al pasar a devuelto el seguimiento le quita el reembolso (no se cobrará). El reenvío tiene que
                // cobrar lo que no se cobró: se recupera de la historia del envío.
                decimal? quitado = await datos.ReembolsoQuitadoPorDevueltoAsync(id);
                if (quitado.HasValue && quitado.Value > 0m)
                {
                    reembolso = quitado.Value;
                    avisos.Add($"El reenvío lleva el reembolso de {reembolso:N2} € que se le quitó al envío {id} al venir devuelto.");
                }
            }

            EnviosAgencia nuevo = Clonar(origen, peticion, reembolso, hoy(), usuario);
            if (string.IsNullOrWhiteSpace(nuevo.Direccion) || string.IsNullOrWhiteSpace(nuevo.CodPostal))
            {
                return ResultadoReenvio.Invalido("Falta la dirección o el código postal del reenvío.");
            }
            string demasiadoLargo = CampoDemasiadoLargo(nuevo);
            if (demasiadoLargo != null)
            {
                return ResultadoReenvio.Invalido(demasiadoLargo);
            }

            // La plaza, fuera de la transacción (GLS puede tardar hasta 15 s). Mismo CP: la del envío devuelto.
            IPlazaYCodigoBarrasAgencia agencia = plazas.Soporta(nuevo.Agencia) ? plazas.Para(nuevo.Agencia) : null;
            bool cambiaCp = !string.Equals(origen.CodPostal?.Trim(), nuevo.CodPostal?.Trim(), StringComparison.OrdinalIgnoreCase);
            if (agencia != null && (cambiaCp || string.IsNullOrWhiteSpace(origen.Nemonico)))
            {
                PlazaAgencia plaza = await agencia.CalcularPlazaAsync(nuevo.CodPostal, nuevo.Pais) ?? PlazaAgencia.Vacia();
                if (!plaza.EsVacia)
                {
                    nuevo.Nemonico = plaza.Nemonico;
                    nuevo.NombrePlaza = plaza.Nombre;
                    nuevo.TelefonoPlaza = PlazaAgencia.RecortarTelefono(plaza.Telefono);
                    nuevo.EmailPlaza = plaza.Email;
                }
                else if (cambiaCp && agencia.PlazaConsultaLaAgencia)
                {
                    avisos.Add($"La agencia no ha dado la plaza del CP {nuevo.CodPostal}: el envío se graba sin plaza.");
                    nuevo.Nemonico = nuevo.NombrePlaza = nuevo.TelefonoPlaza = nuevo.EmailPlaza = string.Empty;
                }
            }
            AgenciaTransporte filaAgencia = agencia != null ? await datos.LeerAgenciaAsync(nuevo.Agencia) : null;

            decimal portes = 0m;
            bool insertado = false;
            try
            {
                await datos.EnTransaccion(async () =>
                {
                    datos.Anadir(nuevo);
                    await datos.GuardarCambios();
                    if (agencia != null)
                    {
                        string codigo = agencia.CalcularCodigoBarras(nuevo, filaAgencia?.PrefijoCodigoBarras);
                        if (codigo == null)
                        {
                            throw new SinCodigoBarrasReenvioException(nuevo.Agencia);
                        }
                        nuevo.CodigoBarras = codigo;
                    }
                    if (peticion.IncluirPortes && nuevo.Pedido.HasValue)
                    {
                        portes = datos.AnadirLineaPortes(nuevo, usuario);
                    }
                    await datos.GuardarCambios();
                    insertado = await datos.Reenvios.InsertarAsync(new RegistroReenvioEnvio
                    {
                        EnvioOrigen = id,
                        Decision = DecisionesReenvio.REENVIAR,
                        EnvioNuevo = nuevo.Numero,
                        Direccion = nuevo.Direccion,
                        CodPostal = nuevo.CodPostal,
                        Poblacion = nuevo.Poblacion,
                        Provincia = nuevo.Provincia,
                        Telefono = nuevo.Telefono,
                        Movil = nuevo.Movil,
                        Observaciones = nuevo.Observaciones,
                        IncluirPortes = peticion.IncluirPortes && nuevo.Pedido.HasValue,
                        Usuario = usuario
                    });
                    if (!insertado)
                    {
                        // Otra sesión ha decidido a la vez: se deshace todo (el envío nuevo y los portes).
                        throw new DecisionYaTomadaException();
                    }
                    return true;
                });
            }
            catch (DecisionYaTomadaException)
            {
                RegistroReenvioEnvio anterior = await datos.Reenvios.LeerAsync(id);
                return ResultadoReenvio.Conflicto(anterior != null ? TextoYaDecidido(id, anterior) : $"El envío {id} ya tiene una decisión.");
            }
            catch (SinCodigoBarrasReenvioException ex)
            {
                return ResultadoReenvio.Invalido(ex.Message);
            }

            if (peticion.IncluirPortes && !nuevo.Pedido.HasValue)
            {
                avisos.Add("El envío no tiene pedido: no se han añadido portes.");
            }
            return ResultadoReenvio.Correcto(new DecisionReenvioDTO
            {
                EnvioOrigen = id,
                Pedido = origen.Pedido,
                Decision = DecisionesReenvio.REENVIAR,
                EnvioNuevo = nuevo.Numero,
                CodigoBarras = nuevo.CodigoBarras?.Trim(),
                PortesIncluidos = portes > 0m,
                Reembolso = nuevo.Reembolso,
                Avisos = avisos,
                Mensaje = $"Envío nuevo {nuevo.Numero} creado: imprima la etiqueta cuando llegue la caja."
            });
        }

        public async Task<ResultadoReenvio> AbonarAsync(int id, string usuario)
        {
            if (!datos.Reenvios.TablaDisponible())
            {
                return ResultadoReenvio.Invalido(MENSAJE_SIN_TABLA);
            }
            EnviosAgencia origen = await datos.LeerEnvioAsync(id);
            ResultadoReenvio error = await Comprobar(id, origen);
            if (error != null)
            {
                return error;
            }
            bool insertado = await datos.Reenvios.InsertarAsync(new RegistroReenvioEnvio
            {
                EnvioOrigen = id,
                Decision = DecisionesReenvio.ABONAR,
                Usuario = usuario
            });
            if (!insertado)
            {
                RegistroReenvioEnvio anterior = await datos.Reenvios.LeerAsync(id);
                return ResultadoReenvio.Conflicto(anterior != null ? TextoYaDecidido(id, anterior) : $"El envío {id} ya tiene una decisión.");
            }
            return ResultadoReenvio.Correcto(new DecisionReenvioDTO
            {
                EnvioOrigen = id,
                Pedido = origen.Pedido,
                Decision = DecisionesReenvio.ABONAR,
                Mensaje = "Marcado para abonar: hágalo con Copiar factura (abono + rehacer si hiciera falta)."
            });
        }

        public async Task<List<EnvioDevueltoDTO>> LeerDevueltosAsync(string empresa)
        {
            if (!datos.Reenvios.TablaDisponible())
            {
                return null;
            }
            List<EnvioAgenciaListadoDTO> envios = await datos.LeerDevueltosEIncidentadosAsync(empresa, hoy().Date.AddDays(-DIAS_LISTADO));
            Dictionary<int, RegistroReenvioEnvio> decisiones = await datos.Reenvios.LeerAsync(envios.Select(e => e.Numero));
            Dictionary<int, short> estadosNuevos = await datos.LeerEstadosAsync(
                decisiones.Values.Where(d => d.EnvioNuevo.HasValue).Select(d => d.EnvioNuevo.Value));
            return Combinar(envios, decisiones, estadosNuevos);
        }

        internal static List<EnvioDevueltoDTO> Combinar(IEnumerable<EnvioAgenciaListadoDTO> envios,
            IDictionary<int, RegistroReenvioEnvio> decisiones, IDictionary<int, short> estadosNuevos)
        {
            var resultado = new List<EnvioDevueltoDTO>();
            foreach (EnvioAgenciaListadoDTO e in envios ?? Enumerable.Empty<EnvioAgenciaListadoDTO>())
            {
                EnvioDevueltoDTO d = CopiarListado(e);
                if (decisiones != null && decisiones.TryGetValue(e.Numero, out RegistroReenvioEnvio decision))
                {
                    d.Decision = decision.Decision?.Trim();
                    d.EnvioNuevo = decision.EnvioNuevo;
                    d.EnvioAnulado = decision.EnvioAnulado;
                    d.PortesIncluidos = decision.IncluirPortes;
                    d.UsuarioDecision = decision.Usuario?.Trim();
                    d.FechaDecision = decision.Fecha;
                    if (decision.EnvioNuevo.HasValue && estadosNuevos != null && estadosNuevos.TryGetValue(decision.EnvioNuevo.Value, out short estado))
                    {
                        d.EstadoEnvioNuevo = estado;
                        d.EnvioNuevoConEtiqueta = estado >= Constantes.Agencias.ESTADO_TRAMITADO;
                    }
                }
                else
                {
                    d.Decision = DecisionesReenvio.SIN_DECIDIR;
                }
                resultado.Add(d);
            }
            // Primero lo que falta decidir; dentro, lo más reciente arriba.
            return resultado
                .OrderBy(d => d.Decision == DecisionesReenvio.SIN_DECIDIR ? 0 : 1)
                .ThenByDescending(d => d.Fecha)
                .ThenByDescending(d => d.Numero)
                .ToList();
        }

        private static EnvioDevueltoDTO CopiarListado(EnvioAgenciaListadoDTO e)
        {
            var d = new EnvioDevueltoDTO();
            foreach (System.Reflection.PropertyInfo p in typeof(EnvioAgenciaListadoDTO).GetProperties())
            {
                if (p.CanRead && p.CanWrite)
                {
                    p.SetValue(d, p.GetValue(e));
                }
            }
            return d;
        }

        /// <summary>
        /// El envío nuevo: mismo pedido, cliente, contacto, agencia, bultos, peso, servicio, horario, retorno, reembolso, nombre y
        /// vendedor; la dirección y los teléfonos, los corregidos si vienen. En curso, con fecha de hoy y sin código de barras ni
        /// estado de la agencia, como un envío recién creado.
        /// </summary>
        internal static EnviosAgencia Clonar(EnviosAgencia o, ReenviarEnvioDTO c, decimal reembolso, DateTime hoy, string usuario)
        {
            var n = new EnviosAgencia
            {
                Empresa = o.Empresa,
                Agencia = o.Agencia,
                Cliente = o.Cliente,
                Contacto = o.Contacto,
                Pedido = o.Pedido,
                Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO,
                Fecha = hoy.Date,
                FechaEntrega = hoy.Date.AddDays(1),
                Servicio = o.Servicio,
                Horario = o.Horario,
                Bultos = o.Bultos,
                Retorno = o.Retorno,
                Nombre = o.Nombre,
                Direccion = Corregido(c.Direccion, o.Direccion),
                CodPostal = Corregido(c.CodPostal, o.CodPostal),
                Poblacion = Corregido(c.Poblacion, o.Poblacion),
                Provincia = Corregido(c.Provincia, o.Provincia),
                Pais = o.Pais,
                Telefono = Corregido(c.Telefono, o.Telefono),
                Movil = Corregido(c.Movil, o.Movil),
                Email = o.Email,
                Observaciones = Corregido(c.Observaciones, o.Observaciones),
                Atencion = o.Atencion,
                Reembolso = reembolso,
                Vendedor = o.Vendedor,
                ImporteGasto = o.ImporteGasto,
                ImporteAsegurado = o.ImporteAsegurado,
                Peso = o.Peso,
                Nemonico = o.Nemonico,
                NombrePlaza = o.NombrePlaza,
                TelefonoPlaza = o.TelefonoPlaza,
                EmailPlaza = o.EmailPlaza,
                CodigoBarras = string.Empty,
                Usuario = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario,
                FechaModificacion = DateTime.Now
            };
            // Como el POST, el PUT y DesdePedido: CP canónico (#596) y textos libres recortados.
            n.CodPostal = EnviosAgenciasController.CodigoPostalEnvio(n.CodPostal, Direcciones.CodigoPostal.PaisIso(n.Pais));
            EnviosAgenciasController.RecortarTextosLibres(n);
            // NestoAPI#778: el reembolso del reparto propio nace pagado (lo cobra el repartidor).
            RepartoPropio.ReembolsoRepartoPropio.AplicarFechaPago(n);
            return n;
        }

        /// <summary>Las columnas de EnviosAgencia: un texto más largo reventaría al grabar.</summary>
        internal static string CampoDemasiadoLargo(EnviosAgencia e)
        {
            var limites = new (string Nombre, string Valor, int Maximo)[]
            {
                ("La dirección", e.Direccion, 80),
                ("El código postal", e.CodPostal, 20),
                ("La población", e.Poblacion, 80),
                ("La provincia", e.Provincia, 80),
                ("El teléfono", e.Telefono, 15),
                ("El móvil", e.Movil, 15)
            };
            foreach (var l in limites)
            {
                if (l.Valor != null && l.Valor.Length > l.Maximo)
                {
                    return $"{l.Nombre} no puede pasar de {l.Maximo} caracteres.";
                }
            }
            return null;
        }

        private static string Corregido(string corregido, string original) => corregido != null ? corregido.Trim() : original;

        /// <summary>El valor que deja en la historia ModificarDatos («147,09 €», en castellano) a número.</summary>
        internal static decimal? LeerImporteHistoria(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }
            string limpio = valor.Replace("€", string.Empty).Replace(" ", " ").Trim();
            return decimal.TryParse(limpio, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, new CultureInfo("es-ES"), out decimal importe)
                ? importe
                : (decimal?)null;
        }

        private sealed class DecisionYaTomadaException : Exception
        {
        }

        private sealed class SinCodigoBarrasReenvioException : Exception
        {
            public SinCodigoBarrasReenvioException(int agencia)
                : base($"La agencia {agencia} no tiene prefijo de código de barras: no se puede crear el reenvío.")
            {
            }
        }
    }

    /// <summary>NestoAPI#699: los datos del servicio de reenvíos contra la BD.</summary>
    public class DatosReenviosEnvioEF : IDatosReenviosEnvio
    {
        private readonly NVEntities db;
        private readonly IAmbitoTransaccion transaccion;
        private readonly Func<GestorPedidosVenta> gestorPedidos;

        public DatosReenviosEnvioEF(NVEntities db, IRepositorioReenviosEnvio reenvios = null, IAmbitoTransaccion transaccion = null,
            Func<GestorPedidosVenta> gestorPedidos = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            Reenvios = reenvios ?? new RepositorioReenviosEnvio(db);
            this.transaccion = transaccion ?? new AmbitoTransaccionDb();
            this.gestorPedidos = gestorPedidos ?? (() => new GestorPedidosVenta(new ServicioPedidosVenta()));
        }

        public IRepositorioReenviosEnvio Reenvios { get; }

        // Where + ToListAsync (no FindAsync): el envío se lee fresco, sin el que pudiera tener ya el contexto.
        public async Task<EnviosAgencia> LeerEnvioAsync(int numero)
            => (await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == numero).ToListAsync()).FirstOrDefault();

        public async Task<AgenciaTransporte> LeerAgenciaAsync(int agencia)
            => (await db.AgenciasTransportes.AsNoTracking().Where(a => a.Numero == agencia).ToListAsync()).FirstOrDefault();

        public async Task<decimal?> ReembolsoQuitadoPorDevueltoAsync(int envio)
        {
            string observaciones = TramitacionEnviosService.OBSERVACIONES_REEMBOLSO_DEVUELTO;
            List<EnvioHistoria> filas = await db.EnviosHistorias.AsNoTracking()
                .Where(h => h.NumeroEnvio == envio && h.Campo == "Reembolso" && h.Observaciones != null && h.Observaciones.Contains(observaciones))
                .OrderByDescending(h => h.FechaModificacion).ThenByDescending(h => h.Numero)
                .ToListAsync();
            return ServicioReenviosEnvio.LeerImporteHistoria(filas.FirstOrDefault()?.ValorAnterior);
        }

        public async Task<List<EnvioAgenciaListadoDTO>> LeerDevueltosEIncidentadosAsync(string empresa, DateTime desde)
        {
            // Más los que la agencia acabó entregando tras decidir reenviarlos: ya no están devueltos ni incidentados, pero el
            // almacén tiene que ver que su reenvío se anuló.
            List<int> entregados = await Reenvios.LeerOrigenesConDecisionAsync(DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA);
            return await EnviosAgenciasController.ProyectarListado(db.EnviosAgencias
                .Where(e => e.Empresa == empresa && e.Fecha >= desde
                    && (e.Estado == Constantes.Agencias.ESTADO_DEVUELTO || e.Estado == Constantes.Agencias.ESTADO_INCIDENTADO
                        || entregados.Contains(e.Numero))))
                .ToListAsync();
        }

        public async Task<Dictionary<int, short>> LeerEstadosAsync(IEnumerable<int> envios)
        {
            List<int> numeros = (envios ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (numeros.Count == 0)
            {
                return new Dictionary<int, short>();
            }
            var filas = await db.EnviosAgencias.AsNoTracking().Where(e => numeros.Contains(e.Numero))
                .Select(e => new { e.Numero, e.Estado }).ToListAsync();
            return filas.ToDictionary(f => f.Numero, f => f.Estado);
        }

        public void Anadir(EnviosAgencia envio) => db.EnviosAgencias.Add(envio);

        public Task GuardarCambios() => db.SaveChangesAsync();

        public Task<T> EnTransaccion<T>(Func<Task<T>> cuerpo) => transaccion.EjecutarAsync(db, cuerpo);

        /// <summary>
        /// La línea de portes como la del picking (<see cref="Models.Picking.GeneradorPortes"/>): cuenta e importe según el CP
        /// del reenvío, en curso y sin picking, para que se facture con el pedido.
        /// </summary>
        public decimal AnadirLineaPortes(EnviosAgencia envio, string usuario)
        {
            bool esProvincial = GestorPortes.EsProvincial(envio.CodPostal);
            string cuenta = esProvincial ? Constantes.Cuentas.CUENTA_PORTES_ONTIME : Constantes.Cuentas.CUENTA_PORTES_CEX;
            decimal portes = esProvincial ? Constantes.Portes.Provincial(DateTime.Today) : Constantes.Portes.PENINSULAR;
            // La empresa del envío puede ser la de la agencia (la espejo); la línea va en la del pedido.
            string empresaPedido = EmpresaDelPedido(envio);
            LinPedidoVta linea = gestorPedidos().CrearLineaVta(empresaPedido, envio.Pedido.Value,
                PedidosVentaController.TIPO_LINEA_CUENTA_CONTABLE, cuenta, 1, portes, UsuarioSinDominio(usuario));
            _ = db.LinPedidoVtas.Add(linea);
            return portes;
        }

        /// <summary>CrearLineaVta le antepone el dominio: «NUEVAVISION\Alfredo» llega como «Alfredo».</summary>
        internal static string UsuarioSinDominio(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return string.Empty;
            }
            int barra = usuario.LastIndexOf('\\');
            return (barra >= 0 ? usuario.Substring(barra + 1) : usuario).Trim();
        }

        private string EmpresaDelPedido(EnviosAgencia envio)
        {
            int pedido = envio.Pedido.Value;
            List<string> empresas = db.CabPedidoVtas.AsNoTracking().Where(c => c.Número == pedido).Select(c => c.Empresa).ToList();
            string propia = empresas.FirstOrDefault(e => e?.Trim() == envio.Empresa?.Trim());
            return (propia ?? empresas.FirstOrDefault() ?? envio.Empresa)?.Trim();
        }
    }
}
