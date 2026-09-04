# [Blog about Actionable Reporting – Alexander Korn](https://actionablereporting.com/ "Blog about Actionable Reporting – Alexander Korn")

# Menu

[Skip to content](https://actionablereporting.com/2023/12/06/power-bi-pimp-script/#content "Skip to content")

- [Home](https://actionablereporting.com/)
- [Fabric Apps](https://actionablereporting.com/fabric-apps/)
- [Fabric](https://actionablereporting.com/#fabric-articles)
- [PBI Fixer](https://actionablereporting.com/pbi-fixer/)
- [Tools](https://actionablereporting.com/#tools-articles)
- [IBCS](https://actionablereporting.com/#ibcs-articles)
- [Best Practices](https://actionablereporting.com/#best-practices-articles)
- [PBIRS](https://actionablereporting.com/#pbirs-articles)
- [Other](https://actionablereporting.com/#other-articles)
- [Featured Works](https://actionablereporting.com/#featured-works)
- [About Me](https://actionablereporting.com/#about-me)
- [Contact](https://actionablereporting.com/#contact)

# Power BI – Pimp – Script

Do you would like to apply **data model best practices** with a click of a button to your existing Power BI reports? Than the “PBI-Pimp-Script” is the right place for you!

**Edit: This script had a major revamp, published on 31. January 2024 and includes now a lot more:** Explicit Measure Creation, Units calc group, Further Calendar Tables, Adding BPA and more.

[image](https://i0.wp.com/actionablereporting.com/wp-content/uploads/2023/12/power-bi-pimp-script.png?resize=750%2C750\&ssl=1)

This script is designed to streamline and enhance your Power BI modeling experience. Whether you are a Power BI data model expert or just getting started, this script helps you supercharge your modeling efforts.

*Overview and Customization*

At the beginning the PBI-Pimp-Script offers through various prompts a range of enhancements that can be customized to fit your specific needs. Let’s dig into key aspects of this script and how you can tailor it to your requirements.

**Calculation Group for Time Intelligence Measures**

One of the essential features of this script is the ability to add a Calculation Group for “Time Intelligence”. Calculation Groups is a great way to organize or even reduce your measures, making it easier to navigate and manage your Power BI model. With this script, you can define a custom name for your Calculation Group, define the name of the date table and date column to be used. This makes sure your Time Intelligence Calculation Group works even if you are using non-standard names. Do you have a fiscal year and need fiscal year calculation items than the script offers the flexibility to adjust the cutoff day. In contrast you don’t need YTD, than decide against it.

**Date Dimension Table**

A robust Date Dimension Table is crucial for time-based analyses in Power BI. The PBI-Pimp-Script allows you to generate a Date Dimension Table and specify its name and the date column name to match your dataset’s structure. This script follows the approach to push the date dimension as far as possible into the backend. For the script this means this is not a calculated table and instead a power query date dimension. You need to make sure the current time selection 2018 till 2025 fits your needs.

**Empty Measure Table**

The script includes an option to generate an Empty Measure Table. Not sure this is the correct name, but that’s how I call this table. The table basically consists of nothing but two columns which are optional to be filled in. Both columns are by default hidden, that means you won’t immediately find this table. The purpose of this table is to work as container for all of your measures. In case the description of the measures is not sufficient, potentially you could also use this table to document your measures in the columns with editing the table directly in Tabular Editor. In case you need additional measure containers, make sure to rerun the script and stating Yes just for the empty measure table question. If you follow tabular modeling best practices than all of your fact tables contain zero visible columns. Therefore Empty Measure Tables is the way to go.

**Last Refresh Table**

Monitoring data refresh times is essential for data-driven decision-making. The script offers the option to create a Last Refresh Table, which keeps track of the last time your data was refreshed. This information can be invaluable for troubleshooting and ensuring that your data is up-to-date. You can use than this table to add a visualization to your report displaying also the last refresh time to your end-user.

**DAX Formatting**

Consistency is key when it comes to DAX (Data Analysis Expressions) formatting. The script allows you to format all calculation items and if you want also all measures in your model, ensuring that your DAX expressions are easy to read and maintain. This feature enhances collaboration and ensures that your entire team follows the same formatting conventions.

**And much more**

Now that you’re familiar with the powerful features of the PBI-Pimp-Script, let’s walk through the manual process of applying it to your Power BI model.

[image](https://i0.wp.com/actionablereporting.com/wp-content/uploads/2024/01/image-1.png?resize=1008%2C567\&ssl=1)

**Manual to Apply the Script**

1. **Connect Tabular Editor (TE2) to PBI Report:** Start by connecting with Tabular Editor (TE2) to your local Power BI instance, your Power BI report opened in PBI Desktop
2. **Save and Reopen .bim Locally with TE2:** To ensure that you have the necessary access to the Power BI model, save and reopen the .bim file locally with TE2.
3. **Apply “Pimp-Script”:** Copy+paste the “Pimp-Script” to enhance your Power BI model. Save it as Macro for reuse. The script will prompt you with various options for customization.
4. **Save PBIP:** Save your Power BI project (PBIP) to preserve your changes.
5. **Ingest Model.bim into the PBIP File:** Copy and replace the updated “Model.bim” into the respective “ReportName.dataset” folder of your Power BI project.
6. **Reopen PBIP File:** Reopen your Power BI project file to see the improvements and enhanced modeling capabilities in action. You might need to apply minor fixes, like the relationship between fact tables and new date dimension.

[image](https://i0.wp.com/actionablereporting.com/wp-content/uploads/2024/01/image.png?resize=1008%2C567\&ssl=1)

[image](https://i0.wp.com/actionablereporting.com/wp-content/uploads/2023/12/image-1.png?resize=1008%2C645\&ssl=1)

I sincerely hope the PBI-Pimp-Script, will help you to apply Power BI data modeling best practices even easier with just a few clicks.

**You need more Power BI data modeling best practices or have ideas to take the script further? –> Ping me via **[**LinkedIn**](https://www.linkedin.com/in/alexanderkorn/)

[**Here is the “Power BI-Pimp-Script”**](https://github.com/KornAlexander/PBI-Tools/blob/main/Power%20BI-Pimp-Script.csx)

---

## Video Walkthrough

[iframe](https://www.youtube.com/embed/-9YaxArn3TM?version=3\&rel=1\&showsearch=0\&showinfo=1\&iv_load_policy=1\&fs=1\&hl=en-US\&autohide=2\&wmode=transparent)

### Video Walkthrough (German)

[iframe](https://www.youtube.com/embed/VBgOTrJ-768?version=3\&rel=1\&showsearch=0\&showinfo=1\&iv_load_policy=1\&fs=1\&hl=en-US\&autohide=2\&wmode=transparent)

### Live Session

[Power BI - Part II  | BI or DIE Level Up - Part IV](https://www.youtube.com/embed/qNtZxnXqPOw?start=2095\&feature=oembed)

### Teilen mit:

- [** X**](https://actionablereporting.com/2023/12/06/power-bi-pimp-script/?share=twitter\&nb=1)
- [** Facebook**](https://actionablereporting.com/2023/12/06/power-bi-pimp-script/?share=facebook\&nb=1)
-

### Like this:

svgLoading…

### *Related*

[My Power BI Toolbox: 80+ Tabular Editor Macros to Automate Data Model Development](https://actionablereporting.com/2024/10/30/my-power-bi-toolbox-80-tabular-editor-macros-to-automate-data-model-development/ "My Power BI Toolbox: 80+ Tabular Editor Macros to Automate Data Model Development")30. October 2024In "Tools"

[PowerShell – Power BI Fixer – Holy Grail of Power BI](https://actionablereporting.com/2025/01/02/power-bi-fixer-holy-grail-of-power-bi/ "PowerShell \&#8211; Power BI Fixer – Holy Grail of Power BI")2. January 2025In "Tools"

[“IBCS Power BI Generator”: Automate your Power BI report development](https://actionablereporting.com/2024/03/28/ibcs-power-bi-implementer-automate-your-power-bi-report-development/ "“IBCS Power BI Generator”: Automate your Power BI report development")28. March 2024In "IBCS"

[6. December 2023](https://actionablereporting.com/2023/12/06/power-bi-pimp-script/ "01:12")[Alexander Korn](https://actionablereporting.com/author/shiporanges/ "View all posts by Alexander Korn") [Actionable Reporting](https://actionablereporting.com/tag/actionable-reporting/), [Best Practice](https://actionablereporting.com/tag/best-practice/), [Calculation Group](https://actionablereporting.com/tag/calculation-group/), [Data Modeling](https://actionablereporting.com/tag/data-modeling/), [power-bi](https://actionablereporting.com/tag/power-bi/), [Tabular Editor](https://actionablereporting.com/tag/tabular-editor/), [Time Intelligence](https://actionablereporting.com/tag/time-intelligence/)

# *Post navigation*

[*← Must-Have Certifications for a Power BI Expert*](https://actionablereporting.com/2023/10/19/must-have-certifications-for-a-power-bi-expert/)

[*Myths about Red-Green Deficiency in Visualizations →*](https://actionablereporting.com/2023/12/21/myths-about-red-green-deficiency-in-visualizations/)

## One thought on “Power BI – Pimp – Script”

1. Pingback: [My Power BI Toolbox: 80+ Tabular Editor Macros to Automate Data Model Development – Blog about Actionable Reporting – Alexander Korn](http://actionablereporting.com/2024/10/30/my-power-bi-toolbox-80-tabular-editor-macros-to-automate-data-model-development/) \~

### *Leave a Reply*

[Comment Form](https://jetpack.wordpress.com/jetpack-comment/?blogid=170451673\&postid=2455\&comment_registration=0\&require_name_email=1\&stc_enabled=1\&stb_enabled=1\&show_avatars=1\&avatar_default=identicon\&greeting=Leave+a+Reply\&jetpack_comments_nonce=a79d44d605\&greeting_reply=Leave+a+Reply+to+%25s\&color_scheme=light\&lang=en_US\&jetpack_version=16.2-a.5\&iframe_unique_id=1\&show_cookie_consent=10\&has_cookie_consent=0\&is_current_user_subscribed=0\&token_key=%3Bnormal%3B\&sig=68c4abf7f75375064aa6772eba6c66dd734e679b#parent=https%3A%2F%2Factionablereporting.com%2F2023%2F12%2F06%2Fpower-bi-pimp-script%2F)

[*Powered by WordPress.com*](https://wordpress.com/?ref=footer_custom_powered)*.*

## Subscribe to Blog via Email

Enter your email address to subscribe to this blog and receive notifications of new posts by email.

Email Address

Subscribe

CLOSE