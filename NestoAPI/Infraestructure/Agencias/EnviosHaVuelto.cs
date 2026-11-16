using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#870: cómo se supo que la caja de un envío ha vuelto al almacén.</summary>
    public static class ComoHaVuelto
    {
        /// <summary>El mozo leyó su etiqueta en Ariadna («Ha vuelto un paquete»).</summary>
        public const string LECTURA = "Lectura";
        /// <summary>Botón «Ya está aquí» de Agencias › Devueltos en Nesto.</summary>
        public const string YA_ESTA_AQUI = "YaEstaAqui";
        /// <summary>Al imprimir o tramitar su reenvío, forzado con «Ya está aquí, imprimir» (yaEstaAqui=true).</summary>
        public const string AL_IMPRIMIR = "AlImprimir";
    }

    /// <summary>NestoAPI#870: una fila de EnviosAgenciaHaVuelto (fuera del EDMX).</summary>
    public class RegistroHaVuelto
    {
        public int Envio { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string Como { get; set; }
        public string Codigo { get; set; }
    }

    /// <summary>NestoAPI#870: la respuesta de POST api/EnviosAgencias/{id}/HaVuelto y de POST api/EnviosAgencias/HaVuelto (Ariadna).</summary>
    public class EnvioHaVueltoDTO
    {
        public int Envio { get; set; }
        public string Empresa { get; set; }
        public int Agencia { get; set; }
        public string NombreAgencia { get; set; }
        public string CodigoBarras { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>El destinatario del envío.</summary>
        public string Nombre { get; set; }
        public string Poblacion { get; set; }
        public short Estado { get; set; }
        public short Bultos { get; set; }
        public DateTime FechaHaVuelto { get; set; }
        public string UsuarioHaVuelto { get; set; }
        /// <summary><see cref="ComoHaVuelto"/>.</summary>
        public string ComoHaVuelto { get; set; }
        /// <summary>Ya estaba marcado de antes: la respuesta trae la marca original y no se ha avisado a nadie.</summary>
        public bool YaEstabaMarcado { get; set; }
        /// <summary><see cref="DecisionesReenvio"/>: SinDecidir, Reenviar, Abonar o EntregadoPorLaAgencia.</summary>
        public string Decision { get; set; }
        public int? EnvioNuevo { get; set; }
        public short? EstadoEnvioNuevo { get; set; }
        /// <summary>Tiene un reenvío sin etiqueta: ahora ya se puede imprimir.</summary>
        public bool ReenvioEsperando { get; set; }
        /// <summary>A quién se ha avisado (quien pidió el reenvío), o null.</summary>
        public string AvisoEnviadoA { get; set; }
        /// <summary>Para el usuario, de usted.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#870: POST api/EnviosAgencias/HaVuelto (Ariadna): lo leído o tecleado.</summary>
    public class LecturaHaVueltoDTO
    {
        public string Codigo { get; set; }
    }

    public enum TipoResultadoHaVuelto
    {
        Correcto,
        /// <summary>400: sin código.</summary>
        Invalido,
        /// <summary>404: no hay envío.</summary>
        NoEncontrado,
        /// <summary>409: no ha salido del almacén, o la etiqueta casa con varios envíos.</summary>
        Conflicto,
        /// <summary>503: falta el script.</summary>
        SinTabla
    }

    public class ResultadoHaVuelto
    {
        public const string CODIGO_NO_HA_SALIDO = "NO_HA_SALIDO";
        public const string CODIGO_VARIOS_ENVIOS = "VARIOS_ENVIOS";

        public TipoResultadoHaVuelto Tipo { get; set; }
        public string Mensaje { get; set; }
        /// <summary>Con <see cref="TipoResultadoHaVuelto.Conflicto"/>: <see cref="CODIGO_NO_HA_SALIDO"/> o <see cref="CODIGO_VARIOS_ENVIOS"/>.</summary>
        public string Codigo { get; set; }
        public EnvioHaVueltoDTO Envio { get; set; }

        public static ResultadoHaVuelto Correcto(EnvioHaVueltoDTO e) => new ResultadoHaVuelto { Tipo = TipoResultadoHaVuelto.Correcto, Envio = e, Mensaje = e?.Mensaje };
        public static ResultadoHaVuelto Invalido(string m) => new ResultadoHaVuelto { Tipo = TipoResultadoHaVuelto.Invalido, Mensaje = m };
        public static ResultadoHaVuelto NoEncontrado(string m) => new ResultadoHaVuelto { Tipo = TipoResultadoHaVuelto.NoEncontrado, Mensaje = m };
        public static ResultadoHaVuelto Conflicto(string codigo, string m) => new ResultadoHaVuelto { Tipo = TipoResultadoHaVuelto.Conflicto, Codigo = codigo, Mensaje = m };
        public static ResultadoHaVuelto SinTabla() => new ResultadoHaVuelto { Tipo = TipoResultadoHaVuelto.SinTabla, Mensaje = ReglasHaVuelto.MENSAJE_SIN_TABLA };
    }

    /// <summary>NestoAPI#870: un reenvío que no puede salir porque la caja de su envío original no ha vuelto.</summary>
    public class ReenvioEsperandoCaja
    {
        /// <summary>Código del 409 de la guarda.</summary>
        public const string CODIGO = "ESPERANDO_CAJA";

        public int EnvioOrigen { get; set; }
        public int EnvioNuevo { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#870: las reglas puras de «ha vuelto» y de la guarda de los reenvíos.</summary>
    public static class ReglasHaVuelto
    {
        internal const string MENSAJE_SIN_TABLA = "Falta lanzar el script Scripts/Issue870_EnviosAgenciaHaVuelto.sql: todavía no se puede " +
            "marcar que ha vuelto la caja de un envío.";

        /// <summary>El Campo de la fila de EnviosHistoria.</summary>
        public const string CAMPO_HISTORIA = "HaVuelto";

        /// <summary>Lo más largo que puede ser el sufijo de bulto de una etiqueta de agencia (CTT, GLS: 3 cifras).</summary>
        internal const int MAXIMO_SUFIJO_BULTO = 4;

        /// <summary>Un código de agencia más corto no se recorta (no se confunde con un número de envío o de pedido).</summary>
        internal const int MINIMO_CODIGO_AGENCIA = 8;

        /// <summary>Lo más largo que se toma como número de envío tecleado (los envíos van por el 250000).</summary>
        internal const int MAXIMO_CIFRAS_NUMERO_ENVIO = 7;

        /// <summary>Lo leído, sin espacios y en mayúsculas.</summary>
        public static string Normalizar(string leido)
            => string.IsNullOrWhiteSpace(leido) ? null : new string(leido.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        /// <summary>
        /// Los EnviosAgencia.CodigoBarras con los que puede casar una lectura: lo leído tal cual y, si acaba en cifras, lo leído sin
        /// 1..4 de ellas (la etiqueta de cada bulto lleva el código del envío más el número de bulto).
        /// </summary>
        public static List<string> CandidatosCodigo(string leido)
        {
            var candidatos = new List<string>();
            string codigo = Normalizar(leido);
            if (codigo == null)
            {
                return candidatos;
            }
            candidatos.Add(codigo);
            for (int k = 1; k <= MAXIMO_SUFIJO_BULTO; k++)
            {
                int resto = codigo.Length - k;
                if (resto < MINIMO_CODIGO_AGENCIA || !char.IsDigit(codigo[resto]))
                {
                    break;
                }
                candidatos.Add(codigo.Substring(0, resto));
            }
            return candidatos;
        }

        /// <summary>Un número de envío tecleado (solo cifras, hasta 7), o null.</summary>
        public static int? NumeroEnvioTecleado(string leido)
        {
            string codigo = Normalizar(leido);
            if (codigo == null || codigo.Length > MAXIMO_CIFRAS_NUMERO_ENVIO || !codigo.All(char.IsDigit))
            {
                return null;
            }
            return int.TryParse(codigo, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : (int?)null;
        }

        /// <summary>
        /// De los envíos que casan con una lectura, el bueno: el del código exacto (si no, el más largo), luego los que han salido,
        /// luego los devueltos o incidentados. Null si quedan varios (se dice cuáles).
        /// </summary>
        public static EnviosAgencia Elegir(IEnumerable<EnviosAgencia> encontrados, string leido, out List<int> empatados)
        {
            empatados = new List<int>();
            List<EnviosAgencia> lista = (encontrados ?? Enumerable.Empty<EnviosAgencia>())
                .Where(e => e != null).GroupBy(e => e.Numero).Select(g => g.First()).ToList();
            if (lista.Count <= 1)
            {
                return lista.FirstOrDefault();
            }
            string codigo = Normalizar(leido);
            int Largo(EnviosAgencia e) => Normalizar(e.CodigoBarras)?.Length ?? 0;
            int mejorLargo = lista.Max(Largo);
            lista = lista.Where(e => Normalizar(e.CodigoBarras) == codigo || Largo(e) == mejorLargo).ToList();
            if (lista.Any(e => Normalizar(e.CodigoBarras) == codigo))
            {
                lista = lista.Where(e => Normalizar(e.CodigoBarras) == codigo).ToList();
            }
            if (lista.Any(HaSalido))
            {
                lista = lista.Where(HaSalido).ToList();
            }
            if (lista.Any(EsDevueltoOIncidentado))
            {
                lista = lista.Where(EsDevueltoOIncidentado).ToList();
            }
            if (lista.Count == 1)
            {
                return lista[0];
            }
            empatados = lista.Select(e => e.Numero).OrderBy(n => n).ToList();
            return null;
        }

        /// <summary>Solo puede volver lo que ha salido: tramitado o después (entregado, incidentado, devuelto…).</summary>
        public static bool HaSalido(EnviosAgencia envio) => envio != null && envio.Estado >= Constantes.Agencias.ESTADO_TRAMITADO;

        private static bool EsDevueltoOIncidentado(EnviosAgencia e)
            => e.Estado == Constantes.Agencias.ESTADO_DEVUELTO || e.Estado == Constantes.Agencias.ESTADO_INCIDENTADO;

        /// <summary>
        /// ¿El envío es un reenvío que tiene que esperar la caja? Decisión Reenviar con ese envío nuevo, el envío nuevo aún sin
        /// tramitar y la caja del original sin volver.
        /// </summary>
        public static bool EsperaLaCaja(RegistroReenvioEnvio filaDelNuevo, int envioNuevo, short estadoNuevo, RegistroHaVuelto vueltaDelOrigen)
            => filaDelNuevo != null
            && filaDelNuevo.Decision?.Trim() == DecisionesReenvio.REENVIAR
            && filaDelNuevo.EnvioNuevo == envioNuevo
            && estadoNuevo < Constantes.Agencias.ESTADO_TRAMITADO
            && vueltaDelOrigen == null;

        /// <summary>¿El PUT hace salir al envío (de pendiente a en curso o tramitado, o de en curso a tramitado)?</summary>
        public static bool PutLoHaceSalir(short estadoAntes, short estadoDespues)
            => (estadoAntes < Constantes.Agencias.ESTADO_EN_CURSO && estadoDespues >= Constantes.Agencias.ESTADO_EN_CURSO)
            || (estadoAntes < Constantes.Agencias.ESTADO_TRAMITADO && estadoDespues >= Constantes.Agencias.ESTADO_TRAMITADO);

        /// <summary>El 409 de la guarda.</summary>
        public static string MensajeEsperandoCaja(int envioOrigen, int envioNuevo)
            => $"La caja del envío {envioOrigen} todavía no ha vuelto al almacén: el envío {envioNuevo} es su reenvío y no puede salir hasta " +
               "que esté aquí. Cuando llegue, léala en Ariadna («Ha vuelto un paquete») o pulse «Ya está aquí» en Agencias › Devueltos.";

        public static string MensajeNoHaSalido(EnviosAgencia e)
            => $"El envío {e.Numero} no ha salido del almacén (está {(e.Estado < Constantes.Agencias.ESTADO_EN_CURSO ? "pendiente" : "en curso")}): " +
               "no puede haber vuelto.";

        public static string MensajeNoEncontrado(string leido)
            => $"No hay ningún envío con la etiqueta «{leido?.Trim()}». Lea la etiqueta de la agencia o teclee el número del envío.";

        public static string MensajeVarios(string leido, IEnumerable<int> envios)
            => $"La etiqueta «{leido?.Trim()}» casa con varios envíos ({string.Join(", ", envios)}): teclee el número del envío " +
               "(o márquelo con «Ya está aquí» en Agencias › Devueltos).";

        /// <summary>La línea de EnviosHistoria.</summary>
        public static string TextoHistoria(string como, string codigo)
        {
            switch (como)
            {
                case ComoHaVuelto.LECTURA:
                    return $"La caja ha vuelto al almacén (leída en Ariadna{(string.IsNullOrWhiteSpace(codigo) ? string.Empty : $": «{codigo.Trim()}»")}).";
                case ComoHaVuelto.AL_IMPRIMIR:
                    return "La caja ha vuelto al almacén («Ya está aquí» al imprimir la etiqueta de su reenvío).";
                default:
                    return "La caja ha vuelto al almacén («Ya está aquí» en Agencias › Devueltos).";
            }
        }

        /// <summary>El texto para quien marca (Ariadna o Nesto), de usted.</summary>
        public static string Mensaje(EnvioHaVueltoDTO d)
        {
            string quien = $"{d.Cliente?.Trim()}/{d.Contacto?.Trim()}" + (string.IsNullOrWhiteSpace(d.Nombre) ? string.Empty : $" {d.Nombre.Trim()}");
            string agencia = string.IsNullOrWhiteSpace(d.NombreAgencia) ? string.Empty : $" de {d.NombreAgencia.Trim()}";
            string cabeza = d.YaEstabaMarcado
                ? $"Ya estaba marcado: la caja del envío {d.Envio}{agencia} ({quien}) volvió el {d.FechaHaVuelto:dd/MM/yyyy} a las {d.FechaHaVuelto:HH:mm}" +
                  (string.IsNullOrWhiteSpace(d.UsuarioHaVuelto) ? "." : $" ({ServicioRecogidasSolicitadas.SinDominio(d.UsuarioHaVuelto)}).")
                : $"La caja del envío {d.Envio}{agencia} ({quien}) ha vuelto al almacén.";
            string decision = d.Decision ?? DecisionesReenvio.SIN_DECIDIR;
            string cola;
            if (decision == DecisionesReenvio.REENVIAR && d.EnvioNuevo.HasValue)
            {
                cola = d.ReenvioEsperando
                    ? $" Ya se puede imprimir la etiqueta del envío {d.EnvioNuevo} (su reenvío)" +
                      (string.IsNullOrWhiteSpace(d.AvisoEnviadoA) ? "." : $"; se ha avisado a {d.AvisoEnviadoA}.")
                    : $" Su reenvío {d.EnvioNuevo} ya había salido: avise a Logística, hay dos envíos del mismo pedido.";
            }
            else if (decision == DecisionesReenvio.ABONAR)
            {
                cola = " Está marcado para abonar: ya se puede hacer el abono.";
            }
            else if (decision == DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA)
            {
                cola = " La agencia lo había dado por entregado y se anuló su reenvío: avise a Logística.";
            }
            else
            {
                cola = " No tiene reenvío: decida en Agencias › Devueltos si se vuelve a enviar o se abona.";
            }
            return cabeza + cola;
        }

        /// <summary>El aviso a quien pidió el reenvío.</summary>
        public static string TituloAviso(EnvioHaVueltoDTO d) => $"Ha vuelto la caja del envío {d.Envio}";

        public static string CuerpoAviso(EnvioHaVueltoDTO d)
            => $"La caja del envío {d.Envio} ha vuelto: ya se puede imprimir la etiqueta del {d.EnvioNuevo}" +
               $" ({d.Cliente?.Trim()}/{d.Contacto?.Trim()}" + (string.IsNullOrWhiteSpace(d.Nombre) ? string.Empty : $" {d.Nombre.Trim()}") +
               (d.Pedido.HasValue ? $", pedido {d.Pedido}" : string.Empty) + ").";
    }

    /// <summary>NestoAPI#870: acceso a EnviosAgenciaHaVuelto, por SQL y comprobando que existe.</summary>
    public interface IRepositorioEnviosHaVuelto
    {
        bool TablaDisponible();
        Task<RegistroHaVuelto> LeerAsync(int envio);
        Task<Dictionary<int, RegistroHaVuelto>> LeerAsync(IEnumerable<int> envios);
        /// <summary>
        /// En una transacción: si el envío no estaba marcado, la fila y su línea en EnviosHistoria. False si ya lo estaba (no
        /// toca nada).
        /// </summary>
        Task<bool> MarcarAsync(RegistroHaVuelto registro, string observacionesHistoria);
    }

    public class RepositorioEnviosHaVuelto : IRepositorioEnviosHaVuelto
    {
        public const string TABLA = "EnviosAgenciaHaVuelto";
        private const string COLUMNAS = "Envio, Fecha, Usuario, Como, Codigo";

        private readonly NVEntities db;

        public RepositorioEnviosHaVuelto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool TablaDisponible() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public async Task<RegistroHaVuelto> LeerAsync(int envio)
        {
            if (!TablaDisponible())
            {
                return null;
            }
            List<RegistroHaVuelto> filas = await db.Database.SqlQuery<RegistroHaVuelto>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Envio = @p0", envio).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public async Task<Dictionary<int, RegistroHaVuelto>> LeerAsync(IEnumerable<int> envios)
        {
            List<int> numeros = (envios ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (numeros.Count == 0 || !TablaDisponible())
            {
                return new Dictionary<int, RegistroHaVuelto>();
            }
            // Enteros: van en el IN sin riesgo de inyección. Pocos (los devueltos de 60 días).
            string lista = string.Join(",", numeros.Select(n => n.ToString(CultureInfo.InvariantCulture)));
            List<RegistroHaVuelto> filas = await db.Database.SqlQuery<RegistroHaVuelto>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE Envio IN ({lista})").ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(f => f.Envio).ToDictionary(g => g.Key, g => g.First());
        }

        public async Task<bool> MarcarAsync(RegistroHaVuelto r, string observacionesHistoria)
        {
            string usuario = Recortar(string.IsNullOrWhiteSpace(r.Usuario) ? "NestoAPI" : r.Usuario.Trim(), 50);
            string sql =
                "SET XACT_ABORT ON; BEGIN TRANSACTION; " +
                $"IF EXISTS (SELECT 1 FROM dbo.{TABLA} WITH (UPDLOCK, HOLDLOCK) WHERE Envio = @p0) " +
                "BEGIN ROLLBACK TRANSACTION; SELECT 0; RETURN; END; " +
                $"INSERT INTO dbo.{TABLA} (Envio, Fecha, Usuario, Como, Codigo) VALUES (@p0, GETDATE(), @p1, @p2, @p3); " +
                "INSERT INTO dbo.EnviosHistoria (NumeroEnvio, Campo, ValorAnterior, Observaciones, Usuario, FechaModificacion) " +
                "VALUES (@p0, @p4, @p5, @p6, @p1, GETDATE()); " +
                "COMMIT TRANSACTION; SELECT 1;";
            List<int> resultado = await db.Database.SqlQuery<int>(sql,
                r.Envio, usuario, Recortar(r.Como, 20), string.IsNullOrWhiteSpace(r.Codigo) ? (object)DBNull.Value : Recortar(r.Codigo.Trim(), 50),
                ReglasHaVuelto.CAMPO_HISTORIA, Recortar(r.Como, 50), observacionesHistoria ?? string.Empty).ToListAsync().ConfigureAwait(false);
            return resultado.FirstOrDefault() == 1;
        }

        private static string Recortar(string texto, int longitud)
            => texto == null ? string.Empty : (texto.Length <= longitud ? texto : texto.Substring(0, longitud));
    }

    /// <summary>NestoAPI#870: lo que lee el servicio de «ha vuelto» (sustituible en los tests).</summary>
    public interface IDatosEnviosHaVuelto
    {
        IRepositorioEnviosHaVuelto HaVuelto { get; }
        IRepositorioReenviosEnvio Reenvios { get; }
        Task<EnviosAgencia> LeerEnvioAsync(int numero);
        /// <summary>Los envíos cuyo CodigoBarras es uno de esos.</summary>
        Task<List<EnviosAgencia>> BuscarPorCodigosAsync(IList<string> codigos);
        Task<string> NombreAgenciaAsync(int agencia);
        /// <summary>La fila Reenviar cuyo envío nuevo es ese, o null.</summary>
        Task<RegistroReenvioEnvio> LeerReenvioPorNuevoAsync(int envioNuevo);
        /// <summary>El estado del envío, o null si no existe.</summary>
        Task<short?> LeerEstadoAsync(int envio);
    }

    public class DatosEnviosHaVueltoEF : IDatosEnviosHaVuelto
    {
        private readonly NVEntities db;

        public DatosEnviosHaVueltoEF(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            HaVuelto = new RepositorioEnviosHaVuelto(db);
            Reenvios = new RepositorioReenviosEnvio(db);
        }

        public IRepositorioEnviosHaVuelto HaVuelto { get; }
        public IRepositorioReenviosEnvio Reenvios { get; }

        public async Task<EnviosAgencia> LeerEnvioAsync(int numero)
            => (await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == numero).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<EnviosAgencia>> BuscarPorCodigosAsync(IList<string> codigos)
        {
            List<string> lista = (codigos ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (lista.Count == 0)
            {
                return Task.FromResult(new List<EnviosAgencia>());
            }
            return db.EnviosAgencias.AsNoTracking().Where(e => lista.Contains(e.CodigoBarras)).ToListAsync();
        }

        public async Task<string> NombreAgenciaAsync(int agencia)
            => (await db.AgenciasTransportes.AsNoTracking().Where(a => a.Numero == agencia).Select(a => a.Nombre).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault()?.Trim();

        public async Task<RegistroReenvioEnvio> LeerReenvioPorNuevoAsync(int envioNuevo)
        {
            if (!Reenvios.TablaDisponible())
            {
                return null;
            }
            List<RegistroReenvioEnvio> filas = await db.Database.SqlQuery<RegistroReenvioEnvio>(
                $"SELECT {RepositorioReenviosEnvio.COLUMNAS} FROM dbo.{RepositorioReenviosEnvio.TABLA} WHERE EnvioNuevo = @p0 AND Decision = @p1",
                envioNuevo, DecisionesReenvio.REENVIAR).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public async Task<short?> LeerEstadoAsync(int envio)
        {
            List<short> estados = await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == envio).Select(e => e.Estado).ToListAsync().ConfigureAwait(false);
            return estados.Count == 0 ? (short?)null : estados[0];
        }
    }

    public interface IServicioEnviosHaVuelto
    {
        /// <summary>«Ya está aquí» de Nesto (o forzado al imprimir): marca el envío por su número.</summary>
        Task<ResultadoHaVuelto> MarcarAsync(int envio, string como, string codigo, string usuario);
        /// <summary>Ariadna: marca el envío de la etiqueta leída (o el número de envío tecleado).</summary>
        Task<ResultadoHaVuelto> MarcarPorLecturaAsync(string codigo, string usuario);
        /// <summary>La guarda: si el envío es un reenvío que espera la caja de su original, el porqué; si no, null.</summary>
        Task<ReenvioEsperandoCaja> EsperandoCajaAsync(int envio);
    }

    /// <summary>
    /// NestoAPI#870 (novedad 1280, Carlos 16/11/26): de un reenvío solo sale lo que está físicamente en Algete. La caja de un envío
    /// devuelto o incidentado «ha vuelto» cuando el mozo la lee en Ariadna, alguien pulsa «Ya está aquí» en Nesto o se fuerza al
    /// imprimir su reenvío. Queda en EnviosAgenciaHaVuelto (fuera del EDMX; una fila por envío, idempotente) y en EnviosHistoria.
    /// Se marca aunque no tenga reenvío (sirve para abonar con certeza). Con reenvío esperando, campana (y push de NestoApp) a quien
    /// lo pidió. La guarda (<see cref="EsperandoCajaAsync"/>) la usan el PUT, Tramitar, ImprimirEtiqueta, EtiquetaZpl,
    /// ConfirmarTramitacion y «Tramitar todos». Sin las tablas, la guarda no actúa y marcar da 503.
    /// </summary>
    public class ServicioEnviosHaVuelto : IServicioEnviosHaVuelto
    {
        private readonly IDatosEnviosHaVuelto datos;
        private readonly IAvisosEnvioHaVuelto avisos;

        public ServicioEnviosHaVuelto(IDatosEnviosHaVuelto datos, IAvisosEnvioHaVuelto avisos)
        {
            this.datos = datos ?? throw new ArgumentNullException(nameof(datos));
            this.avisos = avisos;
        }

        public async Task<ReenvioEsperandoCaja> EsperandoCajaAsync(int envio)
        {
            if (!datos.HaVuelto.TablaDisponible() || !datos.Reenvios.TablaDisponible())
            {
                return null;
            }
            RegistroReenvioEnvio fila = await datos.LeerReenvioPorNuevoAsync(envio).ConfigureAwait(false);
            if (fila == null)
            {
                return null;
            }
            short? estado = await datos.LeerEstadoAsync(envio).ConfigureAwait(false);
            if (!estado.HasValue)
            {
                return null;
            }
            RegistroHaVuelto vuelta = await datos.HaVuelto.LeerAsync(fila.EnvioOrigen).ConfigureAwait(false);
            if (!ReglasHaVuelto.EsperaLaCaja(fila, envio, estado.Value, vuelta))
            {
                return null;
            }
            return new ReenvioEsperandoCaja
            {
                EnvioOrigen = fila.EnvioOrigen,
                EnvioNuevo = envio,
                Mensaje = ReglasHaVuelto.MensajeEsperandoCaja(fila.EnvioOrigen, envio)
            };
        }

        public async Task<ResultadoHaVuelto> MarcarPorLecturaAsync(string codigo, string usuario)
        {
            if (ReglasHaVuelto.Normalizar(codigo) == null)
            {
                return ResultadoHaVuelto.Invalido("Lea la etiqueta del paquete (o teclee el número del envío).");
            }
            if (!datos.HaVuelto.TablaDisponible())
            {
                return ResultadoHaVuelto.SinTabla();
            }
            var encontrados = new List<EnviosAgencia>(await datos.BuscarPorCodigosAsync(ReglasHaVuelto.CandidatosCodigo(codigo)).ConfigureAwait(false));
            int? numero = ReglasHaVuelto.NumeroEnvioTecleado(codigo);
            if (numero.HasValue && encontrados.Count == 0)
            {
                EnviosAgencia porNumero = await datos.LeerEnvioAsync(numero.Value).ConfigureAwait(false);
                if (porNumero != null)
                {
                    encontrados.Add(porNumero);
                }
            }
            EnviosAgencia envio = ReglasHaVuelto.Elegir(encontrados, codigo, out List<int> empatados);
            if (envio == null)
            {
                return empatados.Count > 1
                    ? ResultadoHaVuelto.Conflicto(ResultadoHaVuelto.CODIGO_VARIOS_ENVIOS, ReglasHaVuelto.MensajeVarios(codigo, empatados))
                    : ResultadoHaVuelto.NoEncontrado(ReglasHaVuelto.MensajeNoEncontrado(codigo));
            }
            return await Marcar(envio, ComoHaVuelto.LECTURA, codigo, usuario).ConfigureAwait(false);
        }

        public async Task<ResultadoHaVuelto> MarcarAsync(int envio, string como, string codigo, string usuario)
        {
            if (!datos.HaVuelto.TablaDisponible())
            {
                return ResultadoHaVuelto.SinTabla();
            }
            EnviosAgencia e = await datos.LeerEnvioAsync(envio).ConfigureAwait(false);
            if (e == null)
            {
                return ResultadoHaVuelto.NoEncontrado($"No existe el envío {envio}.");
            }
            return await Marcar(e, string.IsNullOrWhiteSpace(como) ? ComoHaVuelto.YA_ESTA_AQUI : como, codigo, usuario).ConfigureAwait(false);
        }

        private async Task<ResultadoHaVuelto> Marcar(EnviosAgencia envio, string como, string codigo, string usuario)
        {
            if (!ReglasHaVuelto.HaSalido(envio))
            {
                return ResultadoHaVuelto.Conflicto(ResultadoHaVuelto.CODIGO_NO_HA_SALIDO, ReglasHaVuelto.MensajeNoHaSalido(envio));
            }
            RegistroHaVuelto marca = await datos.HaVuelto.LeerAsync(envio.Numero).ConfigureAwait(false);
            bool yaEstaba = marca != null;
            if (!yaEstaba)
            {
                var nueva = new RegistroHaVuelto { Envio = envio.Numero, Usuario = usuario, Como = como, Codigo = codigo };
                bool insertado = await datos.HaVuelto.MarcarAsync(nueva, ReglasHaVuelto.TextoHistoria(como, codigo)).ConfigureAwait(false);
                marca = await datos.HaVuelto.LeerAsync(envio.Numero).ConfigureAwait(false);
                yaEstaba = !insertado;
                if (marca == null)
                {
                    nueva.Fecha = DateTime.Now;
                    marca = nueva;
                }
            }

            var dto = new EnvioHaVueltoDTO
            {
                Envio = envio.Numero,
                Empresa = envio.Empresa?.Trim(),
                Agencia = envio.Agencia,
                NombreAgencia = await datos.NombreAgenciaAsync(envio.Agencia).ConfigureAwait(false),
                CodigoBarras = envio.CodigoBarras?.Trim(),
                Pedido = envio.Pedido,
                Cliente = envio.Cliente?.Trim(),
                Contacto = envio.Contacto?.Trim(),
                Nombre = envio.Nombre?.Trim(),
                Poblacion = envio.Poblacion?.Trim(),
                Estado = envio.Estado,
                Bultos = envio.Bultos,
                FechaHaVuelto = marca.Fecha,
                UsuarioHaVuelto = marca.Usuario?.Trim(),
                ComoHaVuelto = marca.Como?.Trim(),
                YaEstabaMarcado = yaEstaba,
                Decision = DecisionesReenvio.SIN_DECIDIR
            };

            RegistroReenvioEnvio fila = await datos.Reenvios.LeerAsync(envio.Numero).ConfigureAwait(false);
            if (fila != null)
            {
                dto.Decision = fila.Decision?.Trim();
                dto.EnvioNuevo = fila.EnvioNuevo;
                if (fila.EnvioNuevo.HasValue)
                {
                    dto.EstadoEnvioNuevo = await datos.LeerEstadoAsync(fila.EnvioNuevo.Value).ConfigureAwait(false);
                    dto.ReenvioEsperando = dto.Decision == DecisionesReenvio.REENVIAR && dto.EstadoEnvioNuevo.HasValue
                        && dto.EstadoEnvioNuevo.Value < Constantes.Agencias.ESTADO_TRAMITADO;
                }
            }

            if (DebeAvisar(dto, fila, como, usuario))
            {
                dto.AvisoEnviadoA = ServicioRecogidasSolicitadas.SinDominio(fila.Usuario);
                avisos.CajaHaVuelto(dto, fila.Usuario);
            }
            dto.Mensaje = ReglasHaVuelto.Mensaje(dto);
            return ResultadoHaVuelto.Correcto(dto);
        }

        /// <summary>
        /// Se avisa a quien pidió el reenvío la PRIMERA vez, si su reenvío espera la caja, salvo que lo marque él mismo o que se
        /// haya forzado al imprimir (quien imprime ya lo sabe).
        /// </summary>
        internal bool DebeAvisar(EnvioHaVueltoDTO dto, RegistroReenvioEnvio fila, string como, string usuario)
            => avisos != null && !dto.YaEstabaMarcado && dto.ReenvioEsperando && fila != null
            && como != ComoHaVuelto.AL_IMPRIMIR
            && !string.IsNullOrWhiteSpace(fila.Usuario)
            && !ServicioRecogidasSolicitadas.MismoUsuario(fila.Usuario, usuario);
    }

    public interface IAvisosEnvioHaVuelto
    {
        /// <summary>En segundo plano y sin lanzar nunca.</summary>
        void CajaHaVuelto(EnvioHaVueltoDTO envio, string usuarioReenvio);
    }

    /// <summary>
    /// NestoAPI#870: el aviso a quien pidió el reenvío, como los de las recogidas solicitadas (#855): campana de Nesto si lo pidió
    /// desde Nesto (usuario con dominio) y push de NestoApp a su usuario (que también la guarda en su buzón de NestoApp).
    /// </summary>
    public class AvisosEnvioHaVuelto : IAvisosEnvioHaVuelto
    {
        public const string TIPO = "EnvioHaVuelto";

        private readonly Func<IServicioNotificacionesPush> crearNotificaciones;
        private readonly Func<Func<Task>, Task> enSegundoPlano;

        public AvisosEnvioHaVuelto() : this(() => new ServicioNotificacionesPush(), trabajo => Task.Run(trabajo))
        {
        }

        internal AvisosEnvioHaVuelto(Func<IServicioNotificacionesPush> crearNotificaciones, Func<Func<Task>, Task> enSegundoPlano)
        {
            this.crearNotificaciones = crearNotificaciones;
            this.enSegundoPlano = enSegundoPlano ?? (trabajo => trabajo());
        }

        public void CajaHaVuelto(EnvioHaVueltoDTO envio, string usuarioReenvio)
        {
            if (envio == null || string.IsNullOrWhiteSpace(usuarioReenvio))
            {
                return;
            }
            try
            {
                _ = enSegundoPlano(() => Seguro(() => Avisar(envio, usuarioReenvio)));
            }
            catch (Exception ex)
            {
                Registrar(envio, ex);
            }
        }

        internal static NotificacionPushDTO Componer(EnvioHaVueltoDTO envio)
        {
            var datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO,
                ["envio"] = envio.Envio.ToString(CultureInfo.InvariantCulture),
                ["empresa"] = envio.Empresa,
                ["cliente"] = envio.Cliente,
                ["contacto"] = envio.Contacto
            };
            if (envio.EnvioNuevo.HasValue)
            {
                datos["envioNuevo"] = envio.EnvioNuevo.Value.ToString(CultureInfo.InvariantCulture);
            }
            if (envio.Pedido.HasValue)
            {
                datos["pedido"] = envio.Pedido.Value.ToString(CultureInfo.InvariantCulture);
            }
            return new NotificacionPushDTO
            {
                Titulo = ReglasHaVuelto.TituloAviso(envio),
                Cuerpo = ReglasHaVuelto.CuerpoAviso(envio),
                Tipo = TIPO,
                Datos = datos
            };
        }

        internal async Task Avisar(EnvioHaVueltoDTO envio, string usuarioReenvio)
        {
            string usuario = usuarioReenvio.Trim();
            string sinDominio = ServicioRecogidasSolicitadas.SinDominio(usuario);
            if (string.IsNullOrEmpty(sinDominio))
            {
                return;
            }
            NotificacionPushDTO notificacion = Componer(envio);
            IServicioNotificacionesPush notificaciones = crearNotificaciones();
            if (usuario.Contains("\\"))
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            _ = await notificaciones.EnviarAUsuario(sinDominio, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
        }

        private static async Task Seguro(Func<Task> trabajo)
        {
            try
            {
                await trabajo().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Registrar(null, ex);
            }
        }

        private static void Registrar(EnvioHaVueltoDTO envio, Exception ex)
        {
            try
            {
                ElmahHelper.Log(new Exception($"[Ha vuelto #870] No se ha podido avisar de que ha vuelto la caja del envío {envio?.Envio}: {ex.Message}", ex));
            }
            catch
            {
                // nunca romper el marcado
            }
        }
    }
}
