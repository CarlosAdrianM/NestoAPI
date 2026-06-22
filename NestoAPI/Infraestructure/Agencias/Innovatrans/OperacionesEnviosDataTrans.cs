using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>Formato de la etiqueta que devuelve BusquedaEtiquetas: 0 = PDF (por defecto), 1 = ZPL.</summary>
    public enum FormatoEtiquetaDataTrans
    {
        Pdf = 0,
        Zpl = 1
    }

    /// <summary>
    /// Dirección (remitente o destinatario) para DataTrans. La provincia (3 chars) NO se rellena
    /// aquí: se deriva del código postal + país con <see cref="MapeadorDireccionDataTrans"/>.
    /// </summary>
    public class DireccionDataTrans
    {
        public string Pais { get; set; } = MapeadorDireccionDataTrans.PAIS_ESPANA;
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Direccion { get; set; }
    }

    /// <summary>
    /// Datos de un envío a insertar en DataTrans. El albarán NO se incluye (va en blanco: lo asigna
    /// DTX). La provincia de cada dirección se calcula desde el CP. <see cref="TipoServicio"/> es el
    /// código de 4 chars del catálogo (Economy 0048, Portugal 14H 0014, Islas PT 0006, Marítimo 0EXP).
    /// </summary>
    public class EnvioDataTrans
    {
        /// <summary>Departamento del cliente en DataTrans. "00001" es nuestro departamento por defecto;
        /// sin él, según la configuración de la ficha de cliente, DTX puede no devolver el albarán creado.</summary>
        public const string DEPARTAMENTO_POR_DEFECTO = "00001";

        public DireccionDataTrans Remitente { get; set; }
        public DireccionDataTrans Destinatario { get; set; }
        public string Departamento { get; set; } = DEPARTAMENTO_POR_DEFECTO;
        public string Referencia { get; set; }
        public string TipoServicio { get; set; }
        public decimal Largo { get; set; }
        public decimal Alto { get; set; }
        public decimal Ancho { get; set; }
        public decimal PesoReal { get; set; }
        public int Docs { get; set; } = 0;
        public int Paqs { get; set; } = 1;
        public decimal Reembolso { get; set; } = 0;
        public bool ComisionReembolsoPagada { get; set; } = false;
        public bool PortesPagados { get; set; } = true;
        public string Observaciones { get; set; }
    }

    /// <summary>
    /// Resultado de InsertarEnvios. <see cref="Albaran"/> lo asigna DTX (es el identificador con el
    /// que luego se imprime/consulta). El éxito se determina porque DTX devuelve albarán; CodError/
    /// MsgError quedan para diagnóstico (en error, el albarán viene vacío y CodError trae el código).
    /// </summary>
    public class ResultadoInsertarEnvio
    {
        public string Albaran { get; set; }
        public int? CodError { get; set; }
        public string MsgError { get; set; }
        public string Bultos { get; set; }
        public string AgenciaDestino { get; set; }

        /// <summary>
        /// El insert es un éxito solo si DTX asignó un albarán de verdad. DTX señala el fallo de
        /// varias formas que NO debemos tomar por un albarán válido:
        ///  - albarán vacío;
        ///  - un texto de error en &lt;albaran&gt; (p.ej. "ERROR: ...NullPointerException"), pese a codError=200;
        ///  - el placeholder "-" acompañado de un codError de error (p.ej. 402 "No existe agencia
        ///    asociada al país indicado" en envíos a Portugal sin agencia configurada);
        ///  - cualquier codError distinto de 200 (200/vacío = OK; el resto es error de negocio).
        /// Si tratáramos esos casos como éxito, pediríamos la etiqueta de un albarán inexistente y
        /// daríamos un mensaje confuso ("no devolvió etiqueta ZPL") en vez del msgError real.
        /// </summary>
        public bool Exito => !string.IsNullOrWhiteSpace(Albaran)
            && Albaran.Trim() != "-"
            && !Albaran.TrimStart().StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
            && (CodError == null || CodError == 200);
    }

    /// <summary>
    /// Etiqueta devuelta por BusquedaEtiquetas. <see cref="Contenido"/> es el documento (en la
    /// <see cref="Codificacion"/> indicada, normalmente base64); para ZPL hay que decodificarlo antes
    /// de mandarlo a la Zebra. <see cref="Error"/> no vacío indica que no se pudo obtener.
    /// </summary>
    public class EtiquetaDataTrans
    {
        public string Tipo { get; set; }
        public string Codificacion { get; set; }
        public int? TamanoBytes { get; set; }
        public string Contenido { get; set; }
        public string Error { get; set; }

        public bool Exito => string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(Contenido);

        /// <summary>
        /// Una etiqueta ZPL válida empieza por el comando <c>^XA</c>. Innovatrans, cuando NO tiene ZPL
        /// para esa etiqueta, devuelve un PDF (empieza por <c>%PDF</c>) aunque se pida formato=1: lo
        /// detectamos para no mandar basura a la Zebra. Cubre el contenido en crudo y en base64
        /// (<c>^XA</c> → "XlhB", <c>%PDF</c> → "JVBE").
        /// </summary>
        public bool EsZpl
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Contenido)) return false;
                string c = Contenido.TrimStart();
                return c.StartsWith("^XA", StringComparison.Ordinal)
                    || c.StartsWith("XlhB", StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Operaciones de ESCRITURA/etiquetado del WebService DataTrans DTX. CUIDADO: InsertarEnvios crea
    /// un envío REAL en producción (no hay entorno de pruebas). El flujo "registrar al imprimir" de
    /// Innovatrans = InsertarEnvios (DTX asigna albarán + bultos) y luego BusquedaEtiquetas(formato ZPL)
    /// para la Zebra; una reimpresión repite solo BusquedaEtiquetas, sin reinsertar (idempotencia).
    /// </summary>
    public class OperacionesEnviosDataTrans
    {
        private static readonly XNamespace Mes = ClienteSoapDataTrans.Mes;
        private static readonly XNamespace Com = ClienteSoapDataTrans.Com;

        private readonly IClienteSoapDataTrans _cliente;

        public OperacionesEnviosDataTrans(IClienteSoapDataTrans cliente)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        }

        /// <summary>
        /// Inserta un envío. DTX asigna el albarán (va en blanco en la petición). Crea un envío REAL.
        /// </summary>
        public async Task<ResultadoInsertarEnvio> InsertarEnvioAsync(EnvioDataTrans envio)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            if (envio.Remitente == null) throw new ArgumentException("El envío necesita remitente.", nameof(envio));
            if (envio.Destinatario == null) throw new ArgumentException("El envío necesita destinatario.", nameof(envio));

            // OJO ORDEN (XSD de Axis2): albaran -> departamento -> referencia. departamento es
            // imprescindible: sin él DTX puede no devolver el albarán recién creado (lo confirmó
            // el integrador). "00001" es nuestro departamento por defecto.
            XElement envios = new XElement(Mes + "envios",
                new XElement(Com + "albaran"), // en blanco: lo asigna DTX
                Campo("departamento", envio.Departamento),
                Campo("referencia", envio.Referencia));
            AnadirDireccion(envios, "Rem", envio.Remitente);
            AnadirDireccion(envios, "Des", envio.Destinatario);
            // OJO ORDEN: el WS de DataTrans (Axis2) exige el orden EXACTO del XSD. observaciones va
            // tras el bloque del destinatario y ANTES de tipoServ; si no, rechaza con
            // "ADBException: Unexpected subelement observaciones". El resto va en orden de XSD.
            envios.Add(
                Campo("observaciones", envio.Observaciones),
                Campo("tipoServ", envio.TipoServicio),
                Campo("largo", Dec(envio.Largo)),
                Campo("alto", Dec(envio.Alto)),
                Campo("ancho", Dec(envio.Ancho)),
                Campo("pesoReal", Dec(envio.PesoReal)),
                Campo("docs", envio.Docs.ToString(CultureInfo.InvariantCulture)),
                Campo("paqs", envio.Paqs.ToString(CultureInfo.InvariantCulture)),
                Campo("reembolso", Dec(envio.Reembolso)),
                Campo("comReembPag", SiNo(envio.ComisionReembolsoPagada)),
                Campo("portesPagados", SiNo(envio.PortesPagados)));

            XDocument doc = await _cliente.EjecutarAsync("Envios", "InsertarEnvios", envios).ConfigureAwait(false);

            XElement resultado = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "resultado");
            return new ResultadoInsertarEnvio
            {
                Albaran = ValorHijo(resultado, "albaran"),
                CodError = ParsearEntero(ValorHijo(resultado, "codError")),
                MsgError = ValorHijo(resultado, "msgError"),
                Bultos = ValorHijo(resultado, "bultos"),
                AgenciaDestino = ValorHijo(resultado, "ageDestino")
            };
        }

        /// <summary>
        /// Obtiene/reimprime la etiqueta de un albarán ya insertado (no crea ni modifica el envío).
        /// Para la Zebra usar <see cref="FormatoEtiquetaDataTrans.Zpl"/>. desdeBulto/hastaBulto son
        /// opcionales (null = etiqueta completa con todos los bultos).
        /// </summary>
        public async Task<EtiquetaDataTrans> BuscarEtiquetaAsync(
            string albaran,
            FormatoEtiquetaDataTrans formato = FormatoEtiquetaDataTrans.Zpl,
            int? desdeBulto = null,
            int? hastaBulto = null)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));

            XElement parametros = new XElement(Mes + "__placeholder__"); // contenedor temporal
            parametros.Add(new XElement(Mes + "albaran", albaran));
            if (desdeBulto.HasValue) parametros.Add(new XElement(Mes + "desdeBulto", desdeBulto.Value));
            if (hastaBulto.HasValue) parametros.Add(new XElement(Mes + "hastaBulto", hastaBulto.Value));
            parametros.Add(new XElement(Mes + "formato", (int)formato));

            XDocument doc = await _cliente.EjecutarAsync(
                "Etiquetas", "BusquedaEtiquetas", parametros.Elements().ToArray()).ConfigureAwait(false);

            XElement retorno = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "return");
            string contenido = ValorHijo(retorno, "contenido");
            // DTX genera el ZPL con ^CI28 (UTF-8) pero mete los acentos como byte Latin-1 -> la Zebra
            // los descarta. Normalizamos el ZPL a hex UTF-8 (workaround hasta que el integrador lo
            // corrija). Solo aplica al ZPL, no al PDF.
            if (formato == FormatoEtiquetaDataTrans.Zpl)
            {
                contenido = NormalizadorZplDataTrans.CorregirAcentos(contenido);
            }
            return new EtiquetaDataTrans
            {
                Tipo = ValorHijo(retorno, "tipo"),
                Codificacion = ValorHijo(retorno, "codificacion"),
                TamanoBytes = ParsearEntero(ValorHijo(retorno, "tamano")),
                Contenido = contenido,
                Error = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "returnError")?.Value
            };
        }

        private void AnadirDireccion(XElement envios, string sufijo, DireccionDataTrans dir)
        {
            // codPostal y provincia se derivan del CP + país: España va tal cual ("0"+2 dígitos de
            // provincia); Portugal comprime el CP a "6"+4 dígitos en codPostal y deja la provincia vacía.
            string codPostal = MapeadorDireccionDataTrans.CodigoPostalDestino(dir.CodigoPostal, dir.Pais);
            string provincia = MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal(dir.CodigoPostal, dir.Pais);
            envios.Add(
                Campo("pais" + sufijo, dir.Pais),
                Campo("nombre" + sufijo, dir.Nombre),
                Campo("telefono" + sufijo, dir.Telefono),
                Campo("codPostal" + sufijo, codPostal),
                Campo("poblacion" + sufijo, dir.Poblacion),
                Campo("provincia" + sufijo, provincia),
                Campo("direccion" + sufijo, dir.Direccion));
        }

        // Los campos del envío van en el namespace com (hijos de mes:envios).
        private static XElement Campo(string nombre, string valor) => new XElement(Com + nombre, valor ?? string.Empty);

        private static string Dec(decimal valor) => valor.ToString(CultureInfo.InvariantCulture);

        private static string SiNo(bool valor) => valor ? "S" : "N";

        private static string ValorHijo(XElement elemento, string nombreLocal)
            => elemento?.Elements().FirstOrDefault(e => e.Name.LocalName == nombreLocal)?.Value;

        private static int? ParsearEntero(string texto)
            => int.TryParse(texto, out int valor) ? valor : (int?)null;
    }
}
