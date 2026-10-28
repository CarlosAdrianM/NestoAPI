using NestoAPI.Infraestructure.Agencias.Gls;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// 28/10/26 (antes de encender GLS para el packing de Ariadna): la etiqueta de agencia que imprime el servidor (reimpresión
    /// de CTT/Innovatrans, etiqueta de GLS y F8 del packing) salía por ImpresoraCodBarras, la de las etiquetas pequeñas de hueco.
    /// Las de agencia van por las mismas impresoras que usa Nesto: ImpresoraAgenciaGLS para GLS (agencias 1 y 5) e
    /// ImpresoraAgencia para las demás (CTT, Innovatrans); si el usuario no la tiene, la de «(defecto)».
    /// </summary>
    public static class ImpresorasAgencia
    {
        public const string PARAMETRO_IMPRESORA_AGENCIA = "ImpresoraAgencia";
        public const string PARAMETRO_IMPRESORA_GLS = "ImpresoraAgenciaGLS";

        /// <summary>El parámetro de usuario con la impresora de etiquetas de esa agencia.</summary>
        public static string Parametro(int agencia) => AgenciasGls.Es(agencia) ? PARAMETRO_IMPRESORA_GLS : PARAMETRO_IMPRESORA_AGENCIA;
    }
}
