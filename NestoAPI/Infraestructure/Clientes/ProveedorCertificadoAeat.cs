using NestoAPI.Infraestructure.Exceptions;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#388: obtiene el certificado de representante con el que nos autenticamos en los
    /// servicios de la AEAT (VNifV2). Fuente preferente: el almacén de certificados de Windows
    /// (LocalMachine\My), donde la clave privada vive no exportable y renovar el certificado es
    /// solo importar el nuevo en el almacén (Scripts/Certificado/RENOVAR_CERTIFICADO_AEAT.md),
    /// sin tocar código, configuración ni redesplegar: de todos los candidatos vigentes de la
    /// empresa se elige siempre el de caducidad más lejana, así el renovado gana automáticamente.
    /// Fallback transitorio: el .pfx histórico de Infraestructure\Certificados con su contraseña
    /// en secretos.config, cargado UNA sola vez y sin PersistKeySet (el import por llamada del
    /// código anterior dejó más de 12.000 claves huérfanas en MachineKeys del servidor).
    /// </summary>
    public static class ProveedorCertificadoAeat
    {
        internal const string NIF_EMPRESA_POR_DEFECTO = "A78368255";
        internal const int DIAS_AVISO_CADUCIDAD = 15;
        internal const string RUTA_RUNBOOK = @"Scripts\Certificado\RENOVAR_CERTIFICADO_AEAT.md";

        private static readonly Lazy<X509Certificate2> CertificadoPfxFallback =
            new Lazy<X509Certificate2>(CargarPfxFallback);

        private static DateTime _fechaUltimoAvisoCaducidad = DateTime.MinValue;

        public static X509Certificate2 ObtenerCertificado()
        {
            string nifEmpresa = ConfigurationManager.AppSettings["CertificadoAeatNifEmpresa"];
            if (string.IsNullOrWhiteSpace(nifEmpresa))
            {
                nifEmpresa = NIF_EMPRESA_POR_DEFECTO;
            }

            List<CandidatoCertificado> candidatos = LeerCandidatosDelAlmacen();
            if (CertificadoPfxFallback.Value != null)
            {
                candidatos.Add(CandidatoCertificado.DeCertificado(CertificadoPfxFallback.Value));
            }

            CandidatoCertificado elegido = Elegir(candidatos, DateTime.Now, nifEmpresa);
            if (elegido == null)
            {
                throw new NestoBusinessException(
                    $"No hay ningún certificado de la AEAT vigente para {nifEmpresa}: no se pueden " +
                    "validar NIF contra el censo. Hay que importar el certificado renovado en el " +
                    $"almacén de Windows del servidor siguiendo {RUTA_RUNBOOK}.");
            }

            AvisarSiCaducaPronto(elegido);
            return elegido.Certificado;
        }

        internal static CandidatoCertificado Elegir(IEnumerable<CandidatoCertificado> candidatos,
            DateTime ahora, string nifEmpresa)
        {
            return candidatos?
                .Where(c => c != null
                    && c.TieneClavePrivada
                    && EsDeLaEmpresa(c.Subject, nifEmpresa)
                    && c.NotBefore <= ahora && ahora <= c.NotAfter)
                .OrderByDescending(c => c.NotAfter)
                .FirstOrDefault();
        }

        /// <summary>
        /// En el Subject de los certificados FNMT la empresa aparece como organizationIdentifier
        /// ("VATES-A78368255") y/o como marcador de representante ("(R: A78368255)"); basta
        /// cualquiera de los dos para distinguirlo del resto de certificados del almacén (IIS,
        /// RDP, etc.).
        /// </summary>
        internal static bool EsDeLaEmpresa(string subject, string nifEmpresa)
        {
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(nifEmpresa))
            {
                return false;
            }
            string subjectNormalizado = subject.ToUpperInvariant();
            string nif = nifEmpresa.Trim().ToUpperInvariant();
            return subjectNormalizado.Contains($"VATES-{nif}")
                || subjectNormalizado.Contains($"R: {nif}")
                || subjectNormalizado.Contains($"R:{nif}");
        }

        private static List<CandidatoCertificado> LeerCandidatosDelAlmacen()
        {
            var candidatos = new List<CandidatoCertificado>();
            try
            {
                using (var almacen = new X509Store(StoreName.My, StoreLocation.LocalMachine))
                {
                    almacen.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    foreach (X509Certificate2 certificado in almacen.Certificates)
                    {
                        candidatos.Add(CandidatoCertificado.DeCertificado(certificado));
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"CertificadoAeat: no se pudo leer el almacén LocalMachine\\My: {ex.Message}", ex));
            }
            return candidatos;
        }

        private static X509Certificate2 CargarPfxFallback()
        {
            try
            {
                string ruta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    @"Infraestructure\Certificados\cert_cam_nv.pfx");
                if (!File.Exists(ruta))
                {
                    return null;
                }
                string password = ConfigurationManager.AppSettings["CertificadoDigital"];
                // MachineKeySet: bajo IIS no hay perfil de usuario cargado. SIN PersistKeySet: la
                // clave temporal vive lo que viva este estático y no se acumula en MachineKeys.
                return new X509Certificate2(ruta, password, X509KeyStorageFlags.MachineKeySet);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"CertificadoAeat: no se pudo cargar el .pfx de fallback: {ex.Message}", ex));
                return null;
            }
        }

        private static void AvisarSiCaducaPronto(CandidatoCertificado elegido)
        {
            if (elegido.NotAfter > DateTime.Now.AddDays(DIAS_AVISO_CADUCIDAD)
                || _fechaUltimoAvisoCaducidad.Date == DateTime.Today)
            {
                return;
            }
            _fechaUltimoAvisoCaducidad = DateTime.Now;
            ElmahHelper.Log(new Exception(
                $"CertificadoAeat: el certificado de la AEAT caduca el {elegido.NotAfter:dd/MM/yyyy} " +
                $"y no hay otro más nuevo instalado. Renovar siguiendo {RUTA_RUNBOOK}."));
        }
    }

    /// <summary>
    /// Metadatos de un certificado candidato, separados de X509Certificate2 para poder probar la
    /// lógica de elección sin certificados reales (crearlos en runtime no es viable en .NET
    /// Framework).
    /// </summary>
    internal class CandidatoCertificado
    {
        public string Subject { get; set; }
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public bool TieneClavePrivada { get; set; }
        public X509Certificate2 Certificado { get; set; }

        public static CandidatoCertificado DeCertificado(X509Certificate2 certificado)
        {
            return new CandidatoCertificado
            {
                Subject = certificado.Subject,
                NotBefore = certificado.NotBefore,
                NotAfter = certificado.NotAfter,
                TieneClavePrivada = certificado.HasPrivateKey,
                Certificado = certificado
            };
        }
    }
}
