using System;
using System.Globalization;
using System.Text;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#739: la etiqueta del reparto propio, generada por nosotros. ZPL de 4x6" (203 ppp: 812 x 1218 puntos), una por
    /// bulto, con el logo de Nueva Visión (para distinguirla de las de las agencias de fuera), el destinatario, la dirección, el
    /// teléfono, el pedido, «Bulto N/M», el reembolso si lo hay, la fecha y el Code128 <c>RP{EnvioId}-{N}</c> que escanea el
    /// repartidor en Ariadna. Sale por la impresora de agencia (ImpresoraAgencia, la Zebra de Innovatrans). Textos en 1252
    /// (^CI27), que es como los escribe ImpresoraEtiquetasWindows.
    /// </summary>
    public static class EtiquetaRepartoPropio
    {
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <summary>
        /// La ZPL de los bultos <paramref name="desdeBulto"/>..<paramref name="hastaBulto"/> (por defecto, todos).
        /// </summary>
        /// <exception cref="ArgumentException">Sin número de envío o sin bultos.</exception>
        public static string ConstruirZpl(DatosEnvioRemoto envio, DateTime fecha, int? desdeBulto = null, int? hastaBulto = null)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            if (envio.NumeroEnvio <= 0) throw new ArgumentException("El envío no tiene número: no se puede numerar sus bultos.", nameof(envio));
            var plantilla = new PlantillaEtiquetaRepartoPropio
            {
                Titulo = "REPARTO PROPIO",
                Referencia = envio.Referencia,
                Bultos = envio.Bultos,
                CodigoBulto = i => CodigoBultoRepartoPropio.Bulto(envio.NumeroEnvio, i),
                Pie = "Nueva Visión - Envío " + envio.NumeroEnvio.ToString(CultureInfo.InvariantCulture)
            };
            return ConstruirZpl(envio, fecha, plantilla, desdeBulto, hastaBulto);
        }

        /// <summary>
        /// NestoAPI#758: la plantilla común (logo, destinatario, dirección, teléfono, observaciones, «Bulto N/M», reembolso,
        /// Code128 y pie). La usan la etiqueta del envío (RP) y la de los bultos de una nota de recogida (NR), que cambian el
        /// título, la referencia, el código de cada bulto y el pie.
        /// </summary>
        internal static string ConstruirZpl(DatosEnvioRemoto envio, DateTime fecha, PlantillaEtiquetaRepartoPropio plantilla,
            int? desdeBulto = null, int? hastaBulto = null)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            if (plantilla?.CodigoBulto == null) throw new ArgumentNullException(nameof(plantilla));
            int bultos = plantilla.Bultos > 0 ? plantilla.Bultos : 1;
            int desde = Math.Max(1, desdeBulto ?? 1);
            int hasta = Math.Min(bultos, hastaBulto ?? bultos);

            string telefonos = string.Join(" / ", new[] { Texto(envio.Telefono), Texto(envio.Movil) }.WhereNoVacio());
            decimal reembolso = envio.Reembolso > 0 ? envio.Reembolso : 0m;
            var zpl = new StringBuilder();
            for (int i = desde; i <= hasta; i++)
            {
                Linea(zpl, "^XA");
                Linea(zpl, "^CI27");
                Linea(zpl, "^PW812");
                Linea(zpl, "^LL1218");
                Linea(zpl, "^LH0,0");
                Linea(zpl, "^FO20,15" + LogoZplNuevaVision.GRAFICO + "^FS");
                Linea(zpl, "^FO300,25^A0N," + plantilla.AltoTitulo.ToString(CultureInfo.InvariantCulture) + "," +
                    (plantilla.AltoTitulo * 11 / 12).ToString(CultureInfo.InvariantCulture) + "^FD" + Texto(plantilla.Titulo) + "^FS");
                Linea(zpl, "^FO300,85^A0N,32,30^FDPedido: " + Texto(plantilla.Referencia) + "^FS");
                Linea(zpl, "^FO300,125^A0N,32,30^FDFecha: " + fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + "^FS");
                Linea(zpl, "^FO20,165^GB772,3,3^FS");
                Linea(zpl, "^FO20,180^A0N,24,24^FDDESTINATARIO^FS");
                Linea(zpl, "^FO20,210^A0N,44,40^FB772,2,0,L^FD" + Texto(envio.Nombre) + "^FS");
                Linea(zpl, "^FO20,305^A0N,36,32^FB772,2,0,L^FD" + Texto(envio.Direccion) + "^FS");
                Linea(zpl, "^FO20,385^A0N,40,36^FB772,1,0,L^FD" + Texto(envio.CodigoPostal) + " " + Texto(envio.Poblacion) + "^FS");
                Linea(zpl, "^FO20,440^A0N,32,30^FDTel.: " + telefonos + "^FS");
                Linea(zpl, "^FO20,485^A0N,26,24^FB772,2,0,L^FD" + Texto(envio.Observaciones) + "^FS");
                Linea(zpl, "^FO20,550^GB772,3,3^FS");
                Linea(zpl, "^FO20,570^A0N,90,80^FDBulto " + i.ToString(CultureInfo.InvariantCulture) + "/" + bultos.ToString(CultureInfo.InvariantCulture) + "^FS");
                if (reembolso > 0)
                {
                    Linea(zpl, "^FO20,680^GB772,90,4^FS");
                    Linea(zpl, "^FO40,700^A0N,56,50^FDREEMBOLSO: " + reembolso.ToString("N2", Espanol) + " EUR^FS");
                }
                Linea(zpl, "^FO60,810^BY3,3,200^BCN,200,Y,N,N^FD" + plantilla.CodigoBulto(i) + "^FS");
                Linea(zpl, "^FO20,1090^GB772,3,3^FS");
                Linea(zpl, "^FO20,1110^A0N,28,26^FD" + Texto(plantilla.Pie) + "^FS");
                Linea(zpl, "^XZ");
            }
            return zpl.ToString();
        }

        /// <summary>La etiqueta como la devuelven las agencias (ZPL en crudo, como CTT), para Tramitar y la reimpresión.</summary>
        public static Innovatrans.EtiquetaDataTrans ComoEtiqueta(string zpl) => new Innovatrans.EtiquetaDataTrans
        {
            Tipo = "ZPL",
            Codificacion = string.Empty,
            Contenido = zpl
        };

        // «^» y «~» son órdenes de ZPL: un dato con ellos rompería la etiqueta. Los char de la BD llegan con relleno.
        private static string Texto(string valor) => (valor ?? string.Empty).Replace('^', ' ').Replace('~', ' ').Trim();

        private static System.Collections.Generic.IEnumerable<string> WhereNoVacio(this string[] valores)
        {
            foreach (string v in valores)
            {
                if (!string.IsNullOrEmpty(v)) yield return v;
            }
        }

        private static void Linea(StringBuilder zpl, string linea) => zpl.Append(linea).Append("\r\n");
    }

    /// <summary>NestoAPI#758: lo que cambia entre la etiqueta del envío (RP) y la de los bultos de una recogida (NR).</summary>
    internal class PlantillaEtiquetaRepartoPropio
    {
        public string Titulo { get; set; }
        /// <summary>Alto de la letra del título en puntos (48 en la del envío; la de recogida la quiere grande).</summary>
        public int AltoTitulo { get; set; } = 48;
        public string Referencia { get; set; }
        public int Bultos { get; set; }
        /// <summary>El Code128 del bulto N (1..Bultos).</summary>
        public Func<int, string> CodigoBulto { get; set; }
        public string Pie { get; set; }
    }
}
