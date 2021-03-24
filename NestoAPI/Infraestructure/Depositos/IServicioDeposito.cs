using NestoAPI.Models;
using NestoAPI.Models.Depositos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Depositos
{
    public interface IServicioDeposito
    {
        Task<List<ProductoDTO>> LeerProductosProveedor(string proveedorId);
        Task<List<PersonaContactoProveedorDTO>> LeerProveedoresEnDeposito();
        Task<bool> EnviarCorreoSMTP(MailMessage mail);
        Task<DateTime> LeerFechaPrimerVencimiento(string producto);
        Task<int> LeerUnidadesVendidas(string producto);
        Task<int> LeerUnidadesDevueltas(string producto);
        Task<int> LeerUnidadesEnviadasProveedor(string producto);
    }
}