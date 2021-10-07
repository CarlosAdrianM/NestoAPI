using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioCorreoElectronico
    {
        bool EnviarCorreoSMTP(MailMessage mail);
    }
}
