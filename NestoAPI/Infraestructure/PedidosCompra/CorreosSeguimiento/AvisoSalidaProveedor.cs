using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#857e (Carlos 18/11/26): el filtro de texto opcional de un remitente marcado. Un proveedor manda desde la misma dirección
    /// albaranes, facturas y ofertas: con filtro, solo cuentan sus correos que lo dicen en el asunto o en el cuerpo. Varias alternativas
    /// separadas por «|» («albarán de entrega|delivery note»); sin distinguir mayúsculas ni tildes, como palabras enteras. Sin filtro
    /// (null o vacío), todo correo del remitente. PURA.
    /// </summary>
    public static class FiltroRemitente
    {
        public const char SEPARADOR = '|';
        public const int LONGITUD = 500;

        /// <summary>Las alternativas normalizadas (mayúsculas, sin tildes ni signos), sin vacías ni repetidas.</summary>
        public static List<string> Alternativas(string filtro) =>
            (filtro ?? string.Empty).Split(SEPARADOR)
                .Select(TextoCorreo.Normalizar)
                .Where(a => a.Length > 0)
                .Distinct()
                .ToList();

        /// <summary>¿Lo dice el asunto o el cuerpo? Sin filtro (o con un filtro sin ninguna alternativa útil), sí.</summary>
        public static bool Casa(string filtro, string asunto, string texto)
        {
            List<string> alternativas = Alternativas(filtro);
            if (!alternativas.Any())
            {
                return true;
            }
            string donde = " " + TextoCorreo.Normalizar((asunto ?? string.Empty) + "\n" + (texto ?? string.Empty)) + " ";
            return alternativas.Any(a => donde.Contains(" " + a + " "));
        }

        /// <summary>
        /// Para guardarlo: cada alternativa recortada y sin las vacías, unidas por «|»; null si no queda ninguna o si no se queda
        /// ninguna útil (solo signos). Lo que pase de <see cref="LONGITUD"/> no vale (null y el controlador da 400).
        /// </summary>
        public static string Limpiar(string filtro)
        {
            List<string> partes = (filtro ?? string.Empty).Split(SEPARADOR).Select(p => p.Trim()).Where(p => p.Length > 0)
                .Where(p => TextoCorreo.Normalizar(p).Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return partes.Any() ? string.Join(SEPARADOR.ToString(), partes) : null;
        }
    }

    /// <summary>NestoAPI#857e: lo sacado del texto de los PDF de un aviso de salida del proveedor.</summary>
    public class AvisoSalidaPdf
    {
        /// <summary>Nuestro pedido de compra (en el aire) que nombra el PDF, si es uno solo; null si ninguno o varios.</summary>
        public PedidoCompraEnAire Pedido { get; set; }
        /// <summary>La agencia que nombra tras una etiqueta («Compañía de transporte: SCHENKER STANDARD»), o null.</summary>
        public IAgenciaEntrada Agencia { get; set; }
        /// <summary>La entrega que anuncia tras su etiqueta (<see cref="ExtractorEntregaEstimada"/>), o null.</summary>
        public EntregaEstimada Entrega { get; set; }
        /// <summary>NestoAPI#857f: el formato que lo ha leído y los números de envío que da (solo un formato concreto; el genérico, ninguno).</summary>
        public string Formato { get; set; }
        public List<string> Codigos { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#857e: qué dice el PDF de un aviso de salida (packing list, albarán de entrega). PURO, sobre el TEXTO ya extraído (no sabe
    /// nada de PDF). El pedido: un número del texto (token propio de 4-8 cifras, <see cref="AsignadorPedidosCompra.NumerosDePedido"/>) que
    /// es un pedido EN EL AIRE del proveedor del correo (los candidatos del asignador): primero los que llevan etiqueta delante («REF»,
    /// «Pedido», «Su pedido», «PO», «Order»…); si ninguno etiquetado es suyo, cualquier número. Uno solo → ese; varios → ninguno. Sin
    /// proveedor reconocido, solo un número etiquetado que sea de un único pedido en el aire (como en el cuerpo del correo).
    /// </summary>
    public static class ExtractorAvisoSalida
    {
        public static AvisoSalidaPdf Extraer(string texto, DateTime recibido, IEnumerable<PedidoCompraEnAire> delProveedor,
            IEnumerable<PedidoCompraEnAire> enAire, IEnumerable<IAgenciaEntrada> agencias)
        {
            var aviso = new AvisoSalidaPdf();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return aviso;
            }
            // NestoAPI#857f (nivel 1): lo lee su formato (el de su ERP) o, si no lo reconoce ninguno, el genérico (lo de antes)
            IFormatoAvisoSalida formato = FormatosAvisoSalida.Para(texto);
            aviso.Formato = formato.Nombre;
            aviso.Pedido = formato.Pedido(texto, delProveedor, enAire);
            aviso.Agencia = formato.Agencia(texto, agencias ?? AgenciasEntradaRegistradas.Todas);
            aviso.Entrega = formato.Entrega(texto, recibido);
            // Del genérico no se toman códigos del PDF (como antes): un número con «expedición» delante puede ser cualquier cosa
            aviso.Codigos = FormatosAvisoSalida.EsGenerico(formato) ? new List<string>() : formato.Codigos(texto);
            return aviso;
        }

        public static PedidoCompraEnAire Pedido(string texto, IEnumerable<PedidoCompraEnAire> delProveedor, IEnumerable<PedidoCompraEnAire> enAire)
        {
            List<(int Numero, bool Etiquetado)> numeros = AsignadorPedidosCompra.NumerosDePedido(texto);
            if (!numeros.Any())
            {
                return null;
            }
            var etiquetados = new HashSet<int>(numeros.Where(n => n.Etiquetado).Select(n => n.Numero));
            var todos = new HashSet<int>(numeros.Select(n => n.Numero));
            List<PedidoCompraEnAire> suyos = Distintos(delProveedor);
            if (suyos.Any())
            {
                List<PedidoCompraEnAire> conEtiqueta = suyos.Where(p => etiquetados.Contains(p.Pedido)).ToList();
                if (conEtiqueta.Any())
                {
                    return conEtiqueta.Count == 1 ? conEtiqueta[0] : null;
                }
                List<PedidoCompraEnAire> cualquiera = suyos.Where(p => todos.Contains(p.Pedido)).ToList();
                return cualquiera.Count == 1 ? cualquiera[0] : null;
            }
            List<PedidoCompraEnAire> etiquetadosEnAire = Distintos(enAire).Where(p => etiquetados.Contains(p.Pedido)).ToList();
            return etiquetadosEnAire.Count == 1 ? etiquetadosEnAire[0] : null;
        }

        private static List<PedidoCompraEnAire> Distintos(IEnumerable<PedidoCompraEnAire> pedidos) =>
            (pedidos ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null && p.Pedido > 0)
                .GroupBy(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// NestoAPI#857e: junta lo de los PDF con el análisis y la asignación del correo, sin camino paralelo: lo que salga se guarda y se
    /// aplica como cualquier otro correo (fase 3). PURA.
    /// </summary>
    public static class AvisoSalidaProveedor
    {
        /// <summary>
        /// Los pedidos en el aire del proveedor (o proveedores) de los candidatos del asignador: el PDF solo puede elegir entre los suyos.
        /// Vacía si el asignador no ha reconocido al proveedor.
        /// </summary>
        public static List<PedidoCompraEnAire> DelProveedor(IEnumerable<PedidoCompraEnAire> enAire, AsignacionPedidoCompra asignacion)
        {
            var proveedores = new HashSet<string>((asignacion?.Candidatos ?? new List<CandidatoPedidoCompra>())
                .Select(c => c?.Proveedor?.Trim()).Where(p => !string.IsNullOrEmpty(p)), StringComparer.OrdinalIgnoreCase);
            return (enAire ?? Enumerable.Empty<PedidoCompraEnAire>())
                .Where(p => p != null && proveedores.Contains(p.Proveedor?.Trim() ?? string.Empty)).ToList();
        }

        /// <summary>
        /// Con un pedido del PDF, el envío queda INEQUÍVOCO a ese pedido (motivo <see cref="AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO"/>),
        /// aunque el proveedor tenga varios en el aire; sin él, la asignación de siempre. La agencia del PDF solo si el correo no tenía, y
        /// la entrega del PDF solo si el correo no anunciaba ninguna. Devuelve la asignación que vale.
        /// </summary>
        public static AsignacionPedidoCompra Completar(AnalisisCorreoSeguimiento analisis, AsignacionPedidoCompra asignacion, AvisoSalidaPdf pdf)
        {
            if (analisis == null || pdf == null)
            {
                return asignacion;
            }
            if (string.IsNullOrWhiteSpace(analisis.Agencia) && pdf.Agencia != null)
            {
                analisis.Agencia = pdf.Agencia.Nombre;
                analisis.HorasHastaEntrega = pdf.Agencia.HorasHastaEntrega;
                analisis.AgenciaPorNombreEnTexto = true;
            }
            if (analisis.EntregaEstimada == null && pdf.Entrega != null && analisis.EstadoEnvio != EstadoEnvioAgencia.Entregado)
            {
                analisis.EntregaEstimada = pdf.Entrega;
                analisis.OrigenEntrega = OrigenEntregaCorreo.PROVEEDOR;
            }
            // NestoAPI#857f: el número de envío del PDF (solo un formato concreto) si el correo no traía ninguno; con el enlace de su agencia
            foreach (string codigo in pdf.Codigos ?? new List<string>())
            {
                if (!analisis.Seguimientos.Any(s => string.Equals(s.Codigo, codigo, StringComparison.OrdinalIgnoreCase)))
                {
                    analisis.Seguimientos.Add(new SeguimientoCorreo { Codigo = codigo });
                }
            }
            AnalizadorCorreoSeguimiento.CompletarEnlaces(analisis, pdf.Agencia != null ? new[] { pdf.Agencia } : null);
            if (pdf.Pedido == null || analisis.EsSalidaNuestra)
            {
                return asignacion;
            }
            return new AsignacionPedidoCompra
            {
                ProveedorPropuesto = pdf.Pedido.Proveedor?.Trim(),
                Candidatos = new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra
                    {
                        Empresa = pdf.Pedido.Empresa?.Trim(), Pedido = pdf.Pedido.Pedido, Proveedor = pdf.Pedido.Proveedor?.Trim(),
                        Motivo = AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO
                    }
                }
            };
        }

        /// <summary>De varios PDF, el estado que se apunta: el mejor (leído antes que sin texto, ilegible, grande o no descargado).</summary>
        public static EstadoPdfAdjunto EstadoConjunto(IEnumerable<EstadoPdfAdjunto> estados)
        {
            List<EstadoPdfAdjunto> lista = (estados ?? Enumerable.Empty<EstadoPdfAdjunto>()).ToList();
            foreach (EstadoPdfAdjunto preferido in new[] { EstadoPdfAdjunto.Leido, EstadoPdfAdjunto.SinTexto, EstadoPdfAdjunto.Ilegible, EstadoPdfAdjunto.Grande, EstadoPdfAdjunto.NoDescargado })
            {
                if (lista.Contains(preferido))
                {
                    return preferido;
                }
            }
            return EstadoPdfAdjunto.SinPdf;
        }
    }

    /// <summary>NestoAPI#857e: los PDF de un correo, ya leídos (el texto junto solo vive en memoria durante la pasada).</summary>
    public class LecturaPdfsCorreo
    {
        public EstadoPdfAdjunto Estado { get; set; }
        /// <summary>El texto de los PDF leídos, uno detrás de otro; null si no se ha leído ninguno.</summary>
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#857e: un PDF adjunto descargado en memoria (Contenido null = pasa del tope y no se ha descargado).</summary>
    public class PdfAdjunto
    {
        public string Nombre { get; set; }
        public long Tamano { get; set; }
        public byte[] Contenido { get; set; }
    }
}
