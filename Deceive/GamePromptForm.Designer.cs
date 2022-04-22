namespace Deceive
{
    internal partial class GamePromptForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GamePromptForm));
            this.label1 = new System.Windows.Forms.Label();
            this.buttonLaunchLoL = new System.Windows.Forms.Button();
            this.buttonLaunchLoR = new System.Windows.Forms.Button();
            this.buttonLaunchValorant = new System.Windows.Forms.Button();
            this.buttonLaunchRiotClient = new System.Windows.Forms.Button();
            this.checkboxRemember = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.label1.Location = new System.Drawing.Point(156, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(230, 17);
            this.label1.TabIndex = 0;
            this.label1.Text = "Which game would you like to launch?";
            // 
            // buttonLaunchLoL
            // 
            this.buttonLaunchLoL.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchLoL.BackgroundImage")));
            this.buttonLaunchLoL.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchLoL.Location = new System.Drawing.Point(12, 36);
            this.buttonLaunchLoL.Name = "buttonLaunchLoL";
            this.buttonLaunchLoL.Size = new System.Drawing.Size(258, 105);
            this.buttonLaunchLoL.TabIndex = 1;
            this.buttonLaunchLoL.UseVisualStyleBackColor = true;
            this.buttonLaunchLoL.Click += new System.EventHandler(this.OnLoLLaunch);
            // 
            // buttonLaunchLoR
            // 
            this.buttonLaunchLoR.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchLoR.BackgroundImage")));
            this.buttonLaunchLoR.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchLoR.Location = new System.Drawing.Point(276, 36);
            this.buttonLaunchLoR.Name = "buttonLaunchLoR";
            this.buttonLaunchLoR.Size = new System.Drawing.Size(258, 105);
            this.buttonLaunchLoR.TabIndex = 2;
            this.buttonLaunchLoR.UseVisualStyleBackColor = true;
            this.buttonLaunchLoR.Click += new System.EventHandler(this.OnLoRLaunch);
            // 
            // buttonLaunchValorant
            // 
            this.buttonLaunchValorant.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchValorant.BackgroundImage")));
            this.buttonLaunchValorant.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchValorant.Location = new System.Drawing.Point(12, 147);
            this.buttonLaunchValorant.Name = "buttonLaunchValorant";
            this.buttonLaunchValorant.Size = new System.Drawing.Size(258, 105);
            this.buttonLaunchValorant.TabIndex = 3;
            this.buttonLaunchValorant.UseVisualStyleBackColor = true;
            this.buttonLaunchValorant.Click += new System.EventHandler(this.OnValorantLaunch);
            // 
            // buttonLaunchRiotClient
            // 
            this.buttonLaunchRiotClient.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchRiotClient.BackgroundImage")));
            this.buttonLaunchRiotClient.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchRiotClient.Location = new System.Drawing.Point(276, 147);
            this.buttonLaunchRiotClient.Name = "buttonLaunchRiotClient";
            this.buttonLaunchRiotClient.Size = new System.Drawing.Size(258, 105);
            this.buttonLaunchRiotClient.TabIndex = 4;
            this.buttonLaunchRiotClient.UseVisualStyleBackColor = true;
            this.buttonLaunchRiotClient.Click += new System.EventHandler(this.OnRiotClientLaunch);
            // 
            // checkboxRemember
            // 
            this.checkboxRemember.AutoSize = true;
            this.checkboxRemember.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.checkboxRemember.Location = new System.Drawing.Point(15, 260);
            this.checkboxRemember.Name = "checkboxRemember";
            this.checkboxRemember.Size = new System.Drawing.Size(397, 21);
            this.checkboxRemember.TabIndex = 5;
            this.checkboxRemember.Text = "Remember my decision and skip this screen on future launches.";
            this.checkboxRemember.UseVisualStyleBackColor = true;
            // 
            // GamePromptForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(551, 294);
            this.Controls.Add(this.checkboxRemember);
            this.Controls.Add(this.buttonLaunchRiotClient);
            this.Controls.Add(this.buttonLaunchValorant);
            this.Controls.Add(this.buttonLaunchLoR);
            this.Controls.Add(this.buttonLaunchLoL);
            this.Controls.Add(this.label1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "GamePromptForm";
            this.Text = "Deceive";
            this.Load += new System.EventHandler(this.OnFormLoad);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button buttonLaunchLoL;
        private System.Windows.Forms.Button buttonLaunchLoR;
        private System.Windows.Forms.Button buttonLaunchValorant;
        private System.Windows.Forms.Button buttonLaunchRiotClient;
        private System.Windows.Forms.CheckBox checkboxRemember;
    }
}