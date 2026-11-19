using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857: una dirección o un dominio marcado por Compras (tabla AgenciasEntradaRemitentes): de una agencia o, desde #857e
    /// (Scripts/Issue857e), de un PROVEEDOR que avisa de la salida (sin agencia), con filtro de texto y lectura del PDF opcionales.
    /// </summary>
    public class RemitenteMarcado
    {
        public int Id { get; set; }
        /// <summary>Null = remitente de un proveedor (aviso de salida), no de una agencia (#857e).</summary>
        public string Agencia { get; set; }
        /// <summary>Dirección completa (contiene «@») o dominio («ejemplo-transportes.es»), en minúsculas.</summary>
        public string Remitente { get; set; }
        public bool Activo { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
        /// <summary>
        /// NestoAPI#857e: solo cuentan sus correos que lo digan en el asunto o el cuerpo (<see cref="FiltroRemitente"/>; alternativas con
        /// «|»). Null = todos.
        /// </summary>
        public string FiltroTexto { get; set; }
        /// <summary>NestoAPI#857e: leer sus PDF adjuntos (en memoria) para sacar el pedido, la agencia y la entrega.</summary>
        public bool LeerPdf { get; set; }
        /// <summary>
        /// NestoAPI#857e: remitente que no manda nada de envíos (boletines…): sus correos no se guardan, ni se reenvían, ni avisan, traigan lo
        /// que traigan. Se marca con POST …/Remitentes (Ignorar) o al descartar un correo con ignorarRemitente=true.
        /// </summary>
        public bool Ignorar { get; set; }

        /// <summary>¿Casa con esa dirección? Una dirección, exacta; un dominio, también sus subdominios.</summary>
        public bool Casa(string direccion)
        {
            if (string.IsNullOrWhiteSpace(direccion) || string.IsNullOrWhiteSpace(Remitente))
            {
                return false;
            }
            string marcado = Remitente.Trim().ToLowerInvariant();
            return marcado.Contains("@")
                ? string.Equals(direccion.Trim(), marcado, StringComparison.OrdinalIgnoreCase)
                : TextoCorreo.HostDe(TextoCorreo.Dominio(direccion), marcado);
        }
    }

    /// <summary>NestoAPI#857: lo que se ha sacado de un correo.</summary>
    public class AnalisisCorreoSeguimiento
    {
        /// <summary>Solo se guardan los que traen algún seguimiento o vienen de un remitente marcado.</summary>
        public bool Guardar { get; set; }
        /// <summary>La agencia reconocida (o la del remitente marcado); null si solo lo ha captado el genérico y el texto no la nombra tras una etiqueta.</summary>
        public string Agencia { get; set; }
        public int? HorasHastaEntrega { get; set; }
        /// <summary>
        /// La agencia se ha sabido por su nombre tras una etiqueta del texto («Transportista: …»), no por un enlace, el remitente o
        /// un código suyo (los enlaces y códigos los ha sacado el genérico). No se guarda: la tabla no tiene columna para ello.
        /// </summary>
        public bool AgenciaPorNombreEnTexto { get; set; }
        public List<SeguimientoCorreo> Seguimientos { get; set; } = new List<SeguimientoCorreo>();
        public RemitenteMarcado RemitenteMarcado { get; set; }
        /// <summary>Como mucho <see cref="AnalizadorCorreoSeguimiento.LONGITUD_EXTRACTO"/> caracteres alrededor del seguimiento.</summary>
        public string Extracto { get; set; }
        /// <summary>Remitentes originales de un reenvío (líneas «De:» del cuerpo).</summary>
        public List<string> RemitentesOriginales { get; set; } = new List<string>();
        /// <summary>La entrega que anuncia el propio correo (<see cref="ExtractorEntregaEstimada"/>); null si no dice ninguna.</summary>
        public EntregaEstimada EntregaEstimada { get; set; }
        /// <summary>
        /// Es un envío NUESTRO de salida que llega en copia (<see cref="EnvioNuestroDeSalida"/>): se guarda para medir, pero sin
        /// asignación a pedidos de compra ni disponible estimado.
        /// </summary>
        public bool EsSalidaNuestra { get; set; }
        /// <summary>Fase 3: quién expide el paquete según el aviso (<see cref="ExtractorRemitentePaquete"/>), normalizado; null si no lo dice.</summary>
        public string RemitentePaquete { get; set; }
        /// <summary>
        /// NestoAPI#857e: viene de un remitente marcado SIN agencia: un aviso de salida del propio proveedor. Si además no trae código de
        /// seguimiento, su fecha aplicada no cuenta como «seguimiento de la agencia» en la certeza (#853): queda Aproximada.
        /// </summary>
        public bool AvisoProveedor { get; set; }
        /// <summary>NestoAPI#857e: de un remitente marcado como «Ignorar»: no se guarda.</summary>
        public bool Ignorado { get; set; }
        /// <summary>NestoAPI#857e: lo había captado solo el genérico, pero es un boletín comercial: no se guarda.</summary>
        public bool DescartadoPorBoletin { get; set; }
        /// <summary>NestoAPI#857f: el estado del envío según la propia agencia (solo en sus correos); null si no lo dice.</summary>
        public EstadoEnvioAgencia? EstadoEnvio { get; set; }
        /// <summary>NestoAPI#857f: de dónde sale <see cref="EntregaEstimada"/> (<see cref="OrigenEntregaCorreo"/>); null sin entrega.</summary>
        public string OrigenEntrega { get; set; }
        /// <summary>NestoAPI#857f (nivel 1): el formato de aviso del proveedor que lo ha leído (null si es de una agencia). No se guarda.</summary>
        public string FormatoAviso { get; set; }
        /// <summary>NestoAPI#857f: se guarda por ser un correo de la propia agencia que dice la entrega o el estado, aunque no traiga código.</summary>
        public bool GuardadoPorNoticiaDeAgencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#857: clasificación PURA de un correo del buzón de Compras. Orden: las agencias conocidas (la primera que saca un
    /// seguimiento gana); si ninguna, el remitente marcado (con su agencia) y lo que capte el genérico; si tampoco, el genérico solo,
    /// con la agencia que el texto nombre tras una etiqueta (<see cref="AgenciaPorNombreEnTexto"/>) o null. Mira el remitente directo y, en un reenvío, los originales.
    /// </summary>
    public static class AnalizadorCorreoSeguimiento
    {
        public const int LONGITUD_EXTRACTO = 500;

        public static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo, IEnumerable<IAgenciaEntrada> agencias,
            IEnumerable<RemitenteMarcado> marcados, IAgenciaEntrada generica = null)
        {
            var analisis = new AnalisisCorreoSeguimiento();
            // NestoAPI#865: nuestro propio reenvío a Compras (si volviera al buzón que se lee) no se capta otra vez
            if (correo == null || TextosReenvioCorreoSeguimiento.EsReenvioNuestro(correo.Asunto))
            {
                return analisis;
            }
            generica = generica ?? AgenciasEntradaRegistradas.Generica;
            analisis.RemitentesOriginales = TextoCorreo.RemitentesOriginales(correo.Texto);
            var direcciones = new List<string>();
            if (!string.IsNullOrWhiteSpace(correo.RemitenteDireccion))
            {
                direcciones.Add(correo.RemitenteDireccion.Trim().ToLowerInvariant());
            }
            direcciones.AddRange(analisis.RemitentesOriginales);
            List<RemitenteMarcado> activos = (marcados ?? Enumerable.Empty<RemitenteMarcado>()).Where(m => m != null && m.Activo).ToList();
            // NestoAPI#857e: de un remitente marcado como «Ignorar», nada (ni se guarda, ni se reenvía, ni avisa), traiga lo que traiga
            if (activos.Any(m => m.Ignorar && direcciones.Any(m.Casa)))
            {
                analisis.Ignorado = true;
                return analisis;
            }
            // NestoAPI#857e: un remitente con filtro de texto solo cuenta si el asunto o el cuerpo lo dicen (si no, el correo se trata como
            // de un remitente sin marcar: se guarda solo si trae seguimiento)
            analisis.RemitenteMarcado = activos
                .Where(m => direcciones.Any(m.Casa))
                .FirstOrDefault(m => FiltroRemitente.Casa(m.FiltroTexto, correo.Asunto, correo.Texto));
            analisis.AvisoProveedor = analisis.RemitenteMarcado != null && string.IsNullOrWhiteSpace(analisis.RemitenteMarcado.Agencia);

            List<IAgenciaEntrada> lista = (agencias ?? Enumerable.Empty<IAgenciaEntrada>()).ToList();
            foreach (IAgenciaEntrada agencia in lista)
            {
                List<SeguimientoCorreo> seguimientos = agencia.ExtraerSeguimientos(correo) ?? new List<SeguimientoCorreo>();
                if (seguimientos.Any())
                {
                    analisis.Agencia = agencia.Nombre;
                    analisis.HorasHastaEntrega = agencia.HorasHastaEntrega;
                    analisis.Seguimientos = seguimientos;
                    break;
                }
            }
            // NestoAPI#857f: un correo de la PROPIA agencia sin código (SEUR: enlaces opacos) se guarda si dice la entrega o el estado del
            // envío; su publicidad o sus boletines (no dicen nada de un envío) no
            EntregaAgencia noticiaSinCodigo = null;
            if (!analisis.Seguimientos.Any())
            {
                foreach (IAgenciaEntrada agencia in lista.Where(a => a.EsCorreoPropio(correo)))
                {
                    EntregaAgencia noticia = agencia.LeerEntrega(correo);
                    if (noticia != null && noticia.DiceAlgo)
                    {
                        analisis.Agencia = agencia.Nombre;
                        analisis.HorasHastaEntrega = agencia.HorasHastaEntrega;
                        analisis.GuardadoPorNoticiaDeAgencia = true;
                        noticiaSinCodigo = noticia;
                        break;
                    }
                }
            }
            IFormatoAvisoSalida formato = null;
            if (!analisis.Seguimientos.Any() && !analisis.GuardadoPorNoticiaDeAgencia)
            {
                analisis.Seguimientos = generica.ExtraerSeguimientos(correo) ?? new List<SeguimientoCorreo>();
                // NestoAPI#857f (nivel 1): el formato de aviso del proveedor (el de su ERP); sin uno concreto, el genérico (lo de antes)
                formato = FormatosAvisoSalida.Para((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
                if (!FormatosAvisoSalida.EsGenerico(formato))
                {
                    analisis.FormatoAviso = formato.Nombre;
                    var codigos = formato.Codigos((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
                    var delFormato = new List<SeguimientoCorreo>();
                    foreach (string codigo in codigos)
                    {
                        AgenciaEntradaPorPatrones.Anadir(delFormato, codigo, null);
                    }
                    delFormato.AddRange(analisis.Seguimientos.Where(s => !codigos.Contains(s.Codigo ?? string.Empty, StringComparer.OrdinalIgnoreCase)));
                    analisis.Seguimientos = delFormato;
                }
                // NestoAPI#857e: lo que solo capta el genérico en un boletín comercial no es un envío (salvo remitente marcado)
                else if (analisis.Seguimientos.Any() && analisis.RemitenteMarcado == null && BoletinComercial.Es(correo))
                {
                    analisis.Seguimientos = new List<SeguimientoCorreo>();
                    analisis.DescartadoPorBoletin = true;
                }
                if (analisis.RemitenteMarcado != null)
                {
                    analisis.Agencia = analisis.RemitenteMarcado.Agencia?.Trim();
                    analisis.HorasHastaEntrega = AgenciasEntradaRegistradas.PorNombre(analisis.Agencia)?.HorasHastaEntrega;
                }
                if (string.IsNullOrWhiteSpace(analisis.Agencia) && (analisis.Seguimientos.Any() || analisis.AvisoProveedor))
                {
                    // Correo de un proveedor que nombra la agencia tras una etiqueta («Transportista: SCHENKER STANDARD»); también un aviso
                    // de salida de un remitente marcado sin agencia (#857e), aunque no traiga código
                    IAgenciaEntrada porNombre = formato.Agencia((correo.Asunto ?? string.Empty) + "\n" + correo.Texto, lista);
                    if (porNombre != null)
                    {
                        analisis.Agencia = porNombre.Nombre;
                        analisis.HorasHastaEntrega = porNombre.HorasHastaEntrega;
                        analisis.AgenciaPorNombreEnTexto = true;
                    }
                }
            }
            analisis.Guardar = analisis.Seguimientos.Any() || analisis.RemitenteMarcado != null || analisis.GuardadoPorNoticiaDeAgencia;
            if (analisis.Guardar)
            {
                analisis.Extracto = Extracto(correo, analisis.Seguimientos.FirstOrDefault());
                LeerEntrega(correo, analisis, lista, noticiaSinCodigo, formato);
                analisis.EsSalidaNuestra = EnvioNuestroDeSalida.Es((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
                analisis.RemitentePaquete = ExtractorRemitentePaquete.Extraer(correo.Texto, correo.Asunto);
                CompletarEnlaces(analisis, lista);
            }
            return analisis;
        }

        /// <summary>
        /// NestoAPI#857f: la entrega y el estado. Un correo de la PROPIA agencia: lo que ella dice (<see cref="IAgenciaEntrada.LeerEntrega"/>,
        /// origen Agencia). Si no es suyo: lo que dice el aviso (<see cref="IFormatoAvisoSalida.Entrega"/>; sin formato concreto, el extractor
        /// común), origen Proveedor. Un «entregado» no lleva entrega estimada.
        /// </summary>
        private static void LeerEntrega(CorreoCompras correo, AnalisisCorreoSeguimiento analisis, List<IAgenciaEntrada> agencias,
            EntregaAgencia noticia, IFormatoAvisoSalida formato)
        {
            IAgenciaEntrada agencia = Agencia(analisis.Agencia, agencias);
            bool propio = noticia != null || (agencia != null && agencia.EsCorreoPropio(correo));
            if (propio)
            {
                noticia = noticia ?? agencia.LeerEntrega(correo);
                analisis.EstadoEnvio = noticia?.Estado;
                analisis.EntregaEstimada = noticia?.Estado == EstadoEnvioAgencia.Entregado ? null : noticia?.Entrega;
                analisis.OrigenEntrega = analisis.EntregaEstimada != null ? OrigenEntregaCorreo.AGENCIA : null;
                return;
            }
            string texto = formato == null || FormatosAvisoSalida.EsGenerico(formato) ? correo.Texto : (correo.Asunto ?? string.Empty) + "\n" + correo.Texto;
            analisis.EntregaEstimada = (formato ?? FormatosAvisoSalida.Generico).Entrega(texto, correo.Recibido);
            analisis.OrigenEntrega = analisis.EntregaEstimada != null ? OrigenEntregaCorreo.PROVEEDOR : null;
        }

        private static IAgenciaEntrada Agencia(string nombre, IEnumerable<IAgenciaEntrada> agencias) =>
            string.IsNullOrWhiteSpace(nombre) ? null
                : (agencias ?? Enumerable.Empty<IAgenciaEntrada>()).FirstOrDefault(a => string.Equals(a.Nombre, nombre.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? AgenciasEntradaRegistradas.PorNombre(nombre);

        /// <summary>
        /// NestoAPI#857f: a cada código sin enlace, el enlace público de seguimiento que sepa componer SU agencia
        /// (<see cref="IAgenciaEntrada.UrlSeguimiento"/>). Sin agencia o si no sabe, sin enlace.
        /// </summary>
        internal static void CompletarEnlaces(AnalisisCorreoSeguimiento analisis, IEnumerable<IAgenciaEntrada> agencias)
        {
            IAgenciaEntrada agencia = Agencia(analisis?.Agencia, agencias);
            if (agencia == null)
            {
                return;
            }
            foreach (SeguimientoCorreo s in analisis.Seguimientos.Where(s => string.IsNullOrWhiteSpace(s.Url) && !string.IsNullOrWhiteSpace(s.Codigo)))
            {
                s.Url = agencia.UrlSeguimiento(s.Codigo);
            }
        }

        /// <summary>
        /// El texto alrededor del seguimiento: centrado en el código si aparece en el texto; si no, en la primera palabra de
        /// seguimiento; si tampoco, el principio. Nunca el cuerpo entero.
        /// </summary>
        public static string Extracto(CorreoCompras correo, SeguimientoCorreo seguimiento, int longitud = LONGITUD_EXTRACTO)
        {
            string texto = correo?.Texto ?? string.Empty;
            if (texto.Length <= longitud)
            {
                return texto;
            }
            int centro = -1;
            if (!string.IsNullOrEmpty(seguimiento?.Codigo))
            {
                centro = texto.IndexOf(seguimiento.Codigo, StringComparison.OrdinalIgnoreCase);
            }
            if (centro < 0)
            {
                string minusculas = texto.ToLowerInvariant();
                centro = AgenciaEntradaPorPatrones.PalabrasSeguimiento
                    .Select(p => minusculas.IndexOf(p, StringComparison.Ordinal))
                    .Where(i => i >= 0)
                    .DefaultIfEmpty(0)
                    .Min();
            }
            int inicio = Math.Max(0, Math.Min(centro - longitud / 2, texto.Length - longitud));
            return texto.Substring(inicio, longitud).Trim();
        }
    }
}
