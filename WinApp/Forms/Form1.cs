using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using TentacleSoftware.Telnet;

namespace Telnet
{
    //https://www.jerriepelser.com/blog/using-ansi-color-codes-in-net-console-apps/
    //https://github.com/justalemon/consolecontrol
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        public static class ThreadHelperClass
        {
            delegate void SetTextCallback(Form f, Control ctrl, string text);
            public static void SetText(Form form, Control ctrl, string text)
            {
                if (ctrl.InvokeRequired)
                {
                    SetTextCallback d = new SetTextCallback(SetText);
                    form.Invoke(d, [form, ctrl, text]);
                }
                else
                {
                    ctrl.Text = text;
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //load          
            consoleControl1.Font = new Font("Microsoft Sans Serif", 8);
            panel2.Visible = false;
            textBox2.UseSystemPasswordChar = true;
            textBox2.PasswordChar = '●';
            textBox3.Text = "192.168.1.20";
            textBox1.Text = "adminUser";
            textBox2.Text = "adminPassword";
            label1.Text = "&Ready";
        }

        private void HandleMessageReceived(object sender, string message)
        {
            Invoke((MethodInvoker)(() => consoleControl1.InternalRichTextBox.AppendText(StringClass.TrimNonAscii(message) + Environment.NewLine)));
        }
        private void HandleConnectionClosed(object sender, EventArgs e)
        {
            Invoke((MethodInvoker)(() => label1.Text = "Connection closed"));
        }
        public TelnetClient _t;
        private async Task ConnAsync(int start)
        {
            try
            {
                //connect
                panel2.Visible = true;
                groupBox1.Enabled = false;
                groupBox2.Enabled = false;
                var TelnetClass = new TelnetClass();
                consoleControl1.ClearOutput();
                _t = TelnetClass.Login(textBox3.Text, Convert.ToInt32(numericUpDown1.Value));
                if (start == 1)
                {
                    _t.ConnectionClosed += HandleConnectionClosed;
                    _t.MessageReceived += HandleMessageReceived;
                    await _t.Connect();
                    //await _telnetClient.Send("");
                    await _t.Send(textBox1.Text);
                    await _t.Send(textBox2.Text);
                    //await _t.Send("ls /");
                    panel2.Visible = false;
                    groupBox1.Enabled = true;
                    groupBox2.Enabled = true;
                }
                else if (start == 0)
                {
                    //disconnect
                    panel2.Visible = false;
                    groupBox1.Enabled = true;
                    groupBox2.Enabled = true;
                    TelnetClass.Logout(_t);
                    _t.Disconnect();
                }
            }
            catch (Exception ex)
            {
                button2.Enabled = true;
                button3.Enabled = true;
                panel2.Visible = false;
                groupBox1.Enabled = true;
                groupBox2.Enabled = true;
                //MessageBox.Show(ex.Message);
                //MessageBox.Show(ex.ToString());
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            //connect
            await ConnAsync(1);
        }
        private async void button2_Click(object sender, EventArgs e)
        {
            //send
            try
            {
                if (string.IsNullOrWhiteSpace(textBox4.Text))
                {
                    MessageBox.Show("Please enter some text.");
                    //MessageBox.Show(ex.ToString());
                }
                else 
                {
                    //if (_t)
                    //{
                        await _t.Send(textBox4.Text);
                        //MessageBox.Show(_t.ToString());
                    //}
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                //MessageBox.Show(ex.ToString());
            }
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            //disconnect
            button2.Enabled = false;
            button3.Enabled = false;
            await ConnAsync(0);
        }

        private void Form1_Activated(object sender, EventArgs e)
        {
            //activated
            Focus();
            label1.Select();
            SendKeys.Send("%");
        }

        private void Form1_HelpButtonClicked(object sender, CancelEventArgs e)
        {
            Form f2 = new Form2();
            f2.ShowDialog();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            consoleControl1.StartProcess("cmd.exe", "");
        }
    }
}
