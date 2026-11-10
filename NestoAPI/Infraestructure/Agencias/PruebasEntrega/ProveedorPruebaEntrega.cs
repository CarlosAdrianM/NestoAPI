using System;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.PruebasEntrega
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): lo que devuelve cada agencia, en la MISMA forma para todas. El documento va en
    /// bytes con su tipo (PDF o imagen) o, si no se puede traer desde el servidor, como enlace externo (<see cref="UrlDocumento"/>).
    /// Nada se guarda en nuestra BD: se pide a la agencia en el momento (decisión de Carlos).
    /// </summary>
    public class PruebaEntregaAgencia
    {
        /// <summary>False: no hay prueba que ver; <see cref="Mensaje"/> dice por qué (de usted, para el usuario).</summary>
        public bool Disponible { get; set; }
        /// <summary>Con Disponible, una nota (p. ej. «Sin firma (se entregó con DNI).»); sin él, el motivo.</summary>
        public string Mensaje { get; set; }
        public DateTime? FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public byte[] Documento { get; set; }
        /// <summary>El tipo MIME de <see cref="Documento"/>: «application/pdf», «image/png», «image/jpeg»…</summary>
        public string TipoDocumento { get; set; }
        public string UrlDocumento { get; set; }
        /// <summary>Solo el reparto propio: cómo se cobró el reembolso, cuánto y quién registró la entrega.</summary>
        public string CobroReembolso { get; set; }
        public decimal? ImporteCobrado { get; set; }
        public string Usuario { get; set; }

        public static PruebaEntregaAgencia NoDisponible(string mensaje) => new PruebaEntregaAgencia { Disponible = false, Mensaje = mensaje };
    }

    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): la prueba de entrega de un envío de UNA agencia. Cada agencia es una clase
    /// (reparto propio, CTT, GLS) y su perfil la declara con <see cref="Perfiles.IPerfilConPruebaEntrega"/>: el endpoint
    /// (GET api/EnviosAgencias/{id}/PruebaEntrega) y Nesto no saben de agencias. Lo que la agencia no tiene (aún sin prueba,
    /// sin código) se devuelve con <see cref="PruebaEntregaAgencia.NoDisponible"/>; un fallo de la agencia puede lanzar:
    /// <see cref="ServicioPruebasEntrega"/> lo convierte en un mensaje (nunca un 500).
    /// </summary>
    public interface IProveedorPruebaEntrega
    {
        Task<PruebaEntregaAgencia> ObtenerAsync(EnviosAgencia envio);
    }
}
