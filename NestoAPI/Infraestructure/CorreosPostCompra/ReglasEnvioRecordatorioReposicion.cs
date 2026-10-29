using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532: lo que se hace esta semana con el cálculo, después de aplicar bajas, repeticiones y vendedores.</summary>
    public class PlanRecordatorioReposicionDTO
    {
        /// <summary>A estos se les escribe (en Activo) o se les escribiría (en Sombra): vendedor en la lista.</summary>
        public List<RecordatorioReposicionClienteDTO> Enviar { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>Les tocaría, pero su vendedor no está en RecordatorioReposicionVendedores: no se les escribe (como hasta ahora).</summary>
        public List<RecordatorioReposicionClienteDTO> FueraPorVendedor { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>
        /// NestoAPI#532 (Carlos 29/10/26): les tocaría, pero su vendedor de peluquería está en
        /// RecordatorioReposicionExcluirVendedoresPeluqueria: no se les escribe ni van al grupo de control.
        /// </summary>
        public List<RecordatorioReposicionClienteDTO> FueraPorVendedorPeluqueria { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>Grupo de control con el vendedor en la lista: no se les escribe, pero se registran para medir.</summary>
        public List<RecordatorioReposicionClienteDTO> Control { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>Activo: los que han salido de verdad.</summary>
        public List<RecordatorioReposicionClienteDTO> Enviados { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>Activo: los que ha fallado el SMTP (no se registran: la semana que viene vuelven a tocar).</summary>
        public List<RecordatorioReposicionClienteDTO> Fallidos { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>Vendedores de la lista tal como se han leído («(NULL)» = cliente sin vendedor).</summary>
        public List<string> Vendedores { get; set; } = new List<string>();
        /// <summary>Vendedores de peluquería excluidos tal como se han leído (sin fila: «IF»).</summary>
        public List<string> VendedoresPeluqueriaExcluidos { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#532 (modo Activo): qué se hace con lo que calcula <see cref="CalculadoraReposicion"/>. Puro, sin BD.
    /// <list type="number">
    /// <item>Baja: el cliente o su correo está en CorreosPostCompraBajas, de todo o de este tipo → fuera entero.</item>
    /// <item>Un correo cada <see cref="DIAS_ENTRE_CORREOS"/> días como mucho por cliente → fuera entero esta semana.</item>
    /// <item>Un recordatorio por producto hasta que lo vuelva a comprar: si ya se le recordó (o se le apuntó en el grupo de
    /// control) y no lo ha comprado desde entonces, ese producto sale del correo; si no queda ninguno, el cliente entero.</item>
    /// <item>Vendedor de peluquería: si está en RecordatorioReposicionExcluirVendedoresPeluqueria, fuera (ni correo ni control).</item>
    /// <item>Vendedor: solo los clientes cuyo vendedor esté en la lista; el resto, como hasta ahora (no se les escribe).</item>
    /// </list>
    /// Lo mismo para el grupo de control, para que la comparación sea con clientes iguales.
    /// </summary>
    public static class ReglasEnvioRecordatorioReposicion
    {
        public const string SIN_VENDEDOR = "(NULL)";
        public const int DIAS_ENTRE_CORREOS = 14;

        /// <summary>Carlos (29/10/26): «Los que tengan vendedor de peluquería IF, mejor que de momento no salgan.»</summary>
        public const string VENDEDORES_PELUQUERIA_EXCLUIDOS_POR_DEFECTO = "IF";

        public const string MOTIVO_BAJA = "Ha pedido no recibir estos avisos (CorreosPostCompraBajas, de todo o del recordatorio de reposición).";
        public const string MOTIVO_CORREO_RECIENTE = "Ya se le escribió hace menos de 14 días: ";
        public const string MOTIVO_YA_RECORDADO = "Ya se le recordó después de su última compra: ";

        /// <summary>«NV, (NULL), agr» → NV, (NULL), AGR. Vacío o sin fila: ninguno (para que no se escape a todos).</summary>
        public static List<string> LeerVendedores(string valor)
            => (valor ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim().ToUpperInvariant())
                .Where(v => v.Length > 0)
                .Distinct()
                .ToList();

        public static bool VendedorEnLista(string vendedor, IReadOnlyCollection<string> vendedores)
        {
            if (vendedores == null || vendedores.Count == 0)
            {
                return false;
            }
            string clave = string.IsNullOrWhiteSpace(vendedor) ? SIN_VENDEDOR : vendedor.Trim().ToUpperInvariant();
            return vendedores.Contains(clave);
        }

        /// <summary>
        /// NestoAPI#532: vendedores de peluquería excluidos. Null (sin fila o error al leer): «IF». Vacío: ninguno.
        /// </summary>
        public static List<string> LeerVendedoresPeluqueriaExcluidos(string valor)
            => LeerVendedores(valor ?? VENDEDORES_PELUQUERIA_EXCLUIDOS_POR_DEFECTO);

        /// <summary>
        /// NestoAPI#532: true si el vendedor de peluquería del cliente está en la lista de excluidos. Sin vendedor de
        /// peluquería, nunca (aquí «(NULL)» no significa nada: el cliente sigue la regla del de estética).
        /// Solo para el correo semanal (Carlos 29/10/26): la push de #662 sigue llegando a todo el que tiene la app.
        /// </summary>
        public static bool ExcluidoPorVendedorPeluqueria(string vendedorPeluqueria, IReadOnlyCollection<string> excluidos)
            => !string.IsNullOrWhiteSpace(vendedorPeluqueria)
                && excluidos != null
                && excluidos.Contains(vendedorPeluqueria.Trim().ToUpperInvariant());

        /// <param name="vendedoresPeluqueriaExcluidosCsv">RecordatorioReposicionExcluirVendedoresPeluqueria tal cual: null (sin
        /// fila) = «IF»; vacío = ninguno.</param>
        public static PlanRecordatorioReposicionDTO Planificar(ResultadoRecordatorioReposicionDTO resultado, string vendedoresCsv,
            BajasCorreosPostCompra bajas, IReadOnlyCollection<EnvioPrevioReposicion> envios, DateTime hoy,
            string vendedoresPeluqueriaExcluidosCsv = null)
        {
            var plan = new PlanRecordatorioReposicionDTO
            {
                Vendedores = LeerVendedores(vendedoresCsv),
                VendedoresPeluqueriaExcluidos = LeerVendedoresPeluqueriaExcluidos(vendedoresPeluqueriaExcluidosCsv)
            };
            if (resultado == null)
            {
                return plan;
            }
            bajas = bajas ?? new BajasCorreosPostCompra();
            List<EnvioPrevioReposicion> previos = (envios ?? new List<EnvioPrevioReposicion>())
                .Where(e => !string.IsNullOrWhiteSpace(e?.Cliente))
                .ToList();
            Dictionary<string, DateTime> ultimoCorreo = previos
                .Where(e => string.Equals(e.Grupo, RepositorioRecordatorioReposicionSql.GRUPO_ENVIADO, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(e => e.Fecha), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<EnvioPrevioReposicion>> ultimoRecordatorio = previos
                .Where(e => !string.IsNullOrWhiteSpace(e.Producto))
                .GroupBy(e => Clave(e.Cliente, e.Producto), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            DateTime limiteCorreo = hoy.Date.AddDays(-(DIAS_ENTRE_CORREOS - 1));

            foreach (RecordatorioReposicionClienteDTO correo in resultado.SeAvisarian)
            {
                if (!Filtrar(correo, resultado, bajas, ultimoCorreo, ultimoRecordatorio, limiteCorreo))
                {
                    continue;
                }
                if (ExcluidoPorVendedorPeluqueria(correo.VendedorPeluqueria, plan.VendedoresPeluqueriaExcluidos))
                {
                    plan.FueraPorVendedorPeluqueria.Add(correo);
                }
                else
                {
                    (VendedorEnLista(correo.Vendedor, plan.Vendedores) ? plan.Enviar : plan.FueraPorVendedor).Add(correo);
                }
            }
            foreach (RecordatorioReposicionClienteDTO correo in resultado.GrupoControl)
            {
                if (VendedorEnLista(correo.Vendedor, plan.Vendedores)
                    && !ExcluidoPorVendedorPeluqueria(correo.VendedorPeluqueria, plan.VendedoresPeluqueriaExcluidos)
                    && Filtrar(correo, resultado, bajas, ultimoCorreo, ultimoRecordatorio, limiteCorreo))
                {
                    plan.Control.Add(correo);
                }
            }
            return plan;
        }

        /// <summary>Aplica bajas y repeticiones a un correo. False si se queda sin nada (lo quitado va a Descartes con su motivo).</summary>
        private static bool Filtrar(RecordatorioReposicionClienteDTO correo, ResultadoRecordatorioReposicionDTO resultado,
            BajasCorreosPostCompra bajas, Dictionary<string, DateTime> ultimoCorreo, Dictionary<string, List<EnvioPrevioReposicion>> ultimoRecordatorio,
            DateTime limiteCorreo)
        {
            if (bajas.Incluye(correo.Cliente, correo.Email, TiposCorreoCliente.RECORDATORIO_REPOSICION))
            {
                Descartar(resultado, correo.Productos, MOTIVO_BAJA);
                return false;
            }
            if (ultimoCorreo.TryGetValue(correo.Cliente?.Trim() ?? string.Empty, out DateTime ultimo) && ultimo >= limiteCorreo)
            {
                Descartar(resultado, correo.Productos, MOTIVO_CORREO_RECIENTE + ultimo.ToString("dd/MM/yyyy"));
                return false;
            }
            var quedan = new List<CandidatoReposicionDTO>();
            foreach (CandidatoReposicionDTO producto in correo.Productos)
            {
                DateTime? recordado = YaRecordadoSinVolverAComprar(ultimoRecordatorio, correo.Cliente, producto);
                if (recordado.HasValue)
                {
                    Descartar(resultado, new[] { producto }, MOTIVO_YA_RECORDADO + recordado.Value.ToString("dd/MM/yyyy"));
                }
                else
                {
                    quedan.Add(producto);
                }
            }
            correo.Productos = quedan;
            return quedan.Count > 0;
        }

        /// <summary>
        /// Un aviso por producto hasta que lo vuelva a comprar: la fecha del último aviso si no ha comprado después; null si se le
        /// puede avisar. «Volver a comprar» es que su última compra de hoy sea posterior a la que tenía cuando se le avisó (la
        /// guardada en el registro), no a la fecha del aviso: si compra el mismo día que le llega el correo, cuenta como compra
        /// nueva. Sin esa fecha (filas viejas), se compara con el día del aviso, como antes.
        /// </summary>
        private static DateTime? YaRecordadoSinVolverAComprar(Dictionary<string, List<EnvioPrevioReposicion>> previos, string cliente,
            CandidatoReposicionDTO producto)
        {
            if (!previos.TryGetValue(Clave(cliente, producto.Producto), out List<EnvioPrevioReposicion> avisos))
            {
                return null;
            }
            List<EnvioPrevioReposicion> vigentes = avisos
                .Where(a => (a.UltimaCompra ?? a.Fecha).Date >= producto.UltimaCompra.Date)
                .ToList();
            return vigentes.Count == 0 ? (DateTime?)null : vigentes.Max(a => a.Fecha);
        }

        private static void Descartar(ResultadoRecordatorioReposicionDTO resultado, IEnumerable<CandidatoReposicionDTO> productos, string motivo)
        {
            foreach (CandidatoReposicionDTO producto in productos.ToList())
            {
                producto.Motivo = motivo;
                resultado.Descartes.Add(producto);
            }
        }

        private static string Clave(string cliente, string producto) => (cliente?.Trim() ?? string.Empty) + "|" + (producto?.Trim() ?? string.Empty);
    }
}
